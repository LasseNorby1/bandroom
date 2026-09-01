using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bandroom.Api.Data;

/// <summary>
/// Lets `dotnet ef migrations add` run without booting the app or reaching a
/// database — migrations are generated offline and applied as an explicit deploy
/// step (foundation rules, spec §6).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=bandroom-design", npgsql => npgsql.UseNodaTime())
            .Options;
        return new AppDbContext(options, new DesignTimeBandContext());
    }

    private sealed class DesignTimeBandContext : IBandContext
    {
        public Guid? BandId => null;
    }
}
