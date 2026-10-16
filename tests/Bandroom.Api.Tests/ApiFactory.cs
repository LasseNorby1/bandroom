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
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
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

    // RustFS: s3-compatible, apache-2.0, published for amd64 + arm64. MinIO's
    // community images were withdrawn from docker hub and quay in 2026, which
    // took every ci run with them. Pinned tag — never :latest for a test dep.
    private readonly IContainer _s3 = new ContainerBuilder("rustfs/rustfs:1.0.1")
        .WithEnvironment("RUSTFS_ACCESS_KEY", "bandroom")
        .WithEnvironment("RUSTFS_SECRET_KEY", "bandroom_dev")
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
            r.ForPort(9000).ForPath("/").ForStatusCode(System.Net.HttpStatusCode.Forbidden)))
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
                Endpoint = $"http://{_s3.Hostname}:{_s3.GetMappedPublicPort(9000)}",
                Bucket = "bandroom-tests",
                AccessKey = "bandroom",
                SecretKey = "bandroom_dev",
                EnsureBucket = true,
                // Exercises the bucket-cors call against the real s3 server.
                AllowedOrigins = ["http://localhost:3000"],
            }));
        });
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _s3.StartAsync());

        // Touching Services builds the host; migrations run before any test hits it.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _s3.DisposeAsync();
    }
}
