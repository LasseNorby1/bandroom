using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bandroom.Api.Data;

/// <summary>
/// IdentityUserContext, not IdentityDbContext: global Identity roles are useless
/// here — band roles live on memberships (spec §7).
///
/// Tenancy: every IBandScoped entity carries a global query filter pinned to the
/// request's <see cref="IBandContext"/>. The filter references this context
/// instance, so EF re-evaluates it per request — and a null BandId matches
/// nothing (fail closed). Cross-band reads must opt out with IgnoreQueryFilters
/// and their own explicit where-clause.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IBandContext bandContext)
    : IdentityUserContext<AppUser, Guid>(options)
{
    private readonly IBandContext _bandContext = bandContext;

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Band> Bands => Set<Band>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<BandInvite> BandInvites => Set<BandInvite>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(100);
        });

        builder.Entity<RefreshToken>(token =>
        {
            token.Property(t => t.TokenHash).HasMaxLength(64);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.FamilyId);
            token.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Band>(band =>
        {
            band.Property(b => b.Name).HasMaxLength(100);
            band.Property(b => b.TimeZone).HasMaxLength(64);
            band.Property(b => b.RehearsalSpace).HasMaxLength(200);
            band.Property(b => b.DefaultPracticeSlot).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<Membership>(membership =>
        {
            membership.HasIndex(m => new { m.BandId, m.UserId }).IsUnique();
            membership.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
            membership.Property(m => m.Instrument).HasMaxLength(50);
            membership.HasOne<Band>()
                .WithMany()
                .HasForeignKey(m => m.BandId)
                .OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            membership.HasQueryFilter(m => m.BandId == _bandContext.BandId);
        });

        builder.Entity<BandInvite>(invite =>
        {
            invite.Property(i => i.TokenHash).HasMaxLength(64);
            invite.HasIndex(i => i.TokenHash).IsUnique();
            invite.HasOne<Band>()
                .WithMany()
                .HasForeignKey(i => i.BandId)
                .OnDelete(DeleteBehavior.Cascade);
            invite.HasOne<Membership>()
                .WithMany()
                .HasForeignKey(i => i.CreatedByMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            invite.HasQueryFilter(i => i.BandId == _bandContext.BandId);
        });
    }
}
