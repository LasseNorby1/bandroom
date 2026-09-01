using Bandroom.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseNodaTime()));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Touching Services builds the host; migrations run before any test hits it.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
