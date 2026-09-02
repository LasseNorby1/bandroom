using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure.Email;
using Bandroom.Api.Infrastructure.Storage;
using Bandroom.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Xunit;

namespace Bandroom.Api.Tests;

/// <summary>
/// Boots the real API against a real Postgres (Testcontainers) with migrations
/// applied — integration tests go through the actual HTTP surface, no mocking
/// ceremony (foundation rules, spec §6). The DbContext registration is replaced
/// in test services: configuration-level overrides don't reliably beat
/// appsettings.Development.json under minimal hosting.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:latest")
        .WithUsername("bandroom")
        .WithPassword("bandroom_dev")
        .Build();

    public ApiFactory()
    {
        // Env vars are the one config source that reliably beats appsettings under
        // minimal hosting (bug-326) — keep Hangfire and rate limiting out of test
        // hosts (every test shares one client ip and would trip the auth limiter).
        // The worker url just needs to be non-empty so polish jobs can be created;
        // the noop queue never calls it.
        Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
        Environment.SetEnvironmentVariable("RateLimiting__Enabled", "false");
        Environment.SetEnvironmentVariable("Worker__Url", "http://localhost:59999");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseNodaTime()));

            services.RemoveAll<IAppEmailSender>();
            services.AddSingleton<RecordingEmailSender>();
            services.AddSingleton<IAppEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());

            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(_ => new S3FileStorage(new StorageOptions
            {
                Endpoint = _minio.GetConnectionString(),
                Bucket = "bandroom-tests",
                AccessKey = "bandroom",
                SecretKey = "bandroom_dev",
                EnsureBucket = true,
            }));
        });
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());

        // Touching Services builds the host; migrations run before any test hits it.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
    }
}
