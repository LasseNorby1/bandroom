using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Rsvp> Rsvps => Set<Rsvp>();

    public DbSet<Song> Songs => Set<Song>();

    public DbSet<SongIdea> SongIdeas => Set<SongIdea>();

    public DbSet<DemoVersion> DemoVersions => Set<DemoVersion>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Channel> Channels => Set<Channel>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Stem> Stems => Set<Stem>();

    public DbSet<PolishJob> PolishJobs => Set<PolishJob>();

    private static readonly JsonSerializerOptions PatternJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
            band.Property(b => b.Plan).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<Membership>(membership =>
        {
            membership.HasIndex(m => new { m.BandId, m.UserId }).IsUnique();
            membership.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
            membership.Property(m => m.Instrument).HasMaxLength(50);
            membership.Property(m => m.IcsToken).HasMaxLength(64);
            membership.HasIndex(m => m.IcsToken).IsUnique();
            membership.Property(m => m.WeeklyPattern)
                .HasConversion(
                    pattern => JsonSerializer.Serialize(pattern, PatternJsonOptions),
                    json => JsonSerializer.Deserialize<List<WeeklySlot>>(json, PatternJsonOptions) ?? new List<WeeklySlot>(),
                    new ValueComparer<List<WeeklySlot>>(
                        (a, b) => (a ?? new List<WeeklySlot>()).SequenceEqual(b ?? new List<WeeklySlot>()),
                        v => v.Aggregate(0, (hash, slot) => HashCode.Combine(hash, slot.GetHashCode())),
                        v => v.ToList()))
                .HasColumnType("jsonb");
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

        builder.Entity<AvailabilityException>(exception =>
        {
            exception.Property(e => e.Note).HasMaxLength(200);
            exception.HasIndex(e => new { e.MembershipId, e.To });
            exception.HasOne<Membership>()
                .WithMany()
                .HasForeignKey(e => e.MembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            exception.HasQueryFilter(e => e.BandId == _bandContext.BandId);
        });

        builder.Entity<Event>(evt =>
        {
            evt.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            evt.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            evt.Property(e => e.Slot).HasConversion<string>().HasMaxLength(20);
            evt.Property(e => e.Title).HasMaxLength(120);
            evt.Property(e => e.Location).HasMaxLength(200);
            evt.HasIndex(e => new { e.BandId, e.Date });
            evt.HasOne<Band>()
                .WithMany()
                .HasForeignKey(e => e.BandId)
                .OnDelete(DeleteBehavior.Cascade);
            evt.HasOne<Membership>()
                .WithMany()
                .HasForeignKey(e => e.CreatedByMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            evt.HasQueryFilter(e => e.BandId == _bandContext.BandId);
        });

        builder.Entity<Rsvp>(rsvp =>
        {
            rsvp.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            rsvp.Property(r => r.Note).HasMaxLength(200);
            rsvp.HasIndex(r => new { r.EventId, r.MembershipId }).IsUnique();
            rsvp.HasOne<Event>()
                .WithMany()
                .HasForeignKey(r => r.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            rsvp.HasOne<Membership>()
                .WithMany()
                .HasForeignKey(r => r.MembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            rsvp.HasQueryFilter(r => r.BandId == _bandContext.BandId);
        });

        builder.Entity<Song>(song =>
        {
            song.Property(s => s.Title).HasMaxLength(120);
            song.Property(s => s.Key).HasMaxLength(12);
            song.Property(s => s.Notes).HasMaxLength(2000);
            song.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            song.HasOne<Band>().WithMany().HasForeignKey(s => s.BandId).OnDelete(DeleteBehavior.Cascade);
            song.HasQueryFilter(s => s.BandId == _bandContext.BandId);
        });

        builder.Entity<SongIdea>(idea =>
        {
            idea.Property(i => i.Title).HasMaxLength(120);
            idea.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            idea.HasOne<Band>().WithMany().HasForeignKey(i => i.BandId).OnDelete(DeleteBehavior.Cascade);
            idea.HasOne<Song>().WithMany().HasForeignKey(i => i.SongId).OnDelete(DeleteBehavior.SetNull);
            idea.HasOne<Membership>().WithMany().HasForeignKey(i => i.CreatedByMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            idea.HasQueryFilter(i => i.BandId == _bandContext.BandId);
        });

        builder.Entity<DemoVersion>(version =>
        {
            version.Property(v => v.FileKey).HasMaxLength(300);
            version.Property(v => v.FileName).HasMaxLength(200);
            version.Property(v => v.ContentType).HasMaxLength(100);
            version.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
            version.Property(v => v.Kind).HasConversion<string>().HasMaxLength(20);
            version.HasIndex(v => new { v.SongIdeaId, v.Number }).IsUnique();
            version.HasOne<SongIdea>().WithMany().HasForeignKey(v => v.SongIdeaId)
                .OnDelete(DeleteBehavior.Cascade);
            version.HasOne<Membership>().WithMany().HasForeignKey(v => v.UploadedByMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            version.HasQueryFilter(v => v.BandId == _bandContext.BandId);
        });

        builder.Entity<Comment>(comment =>
        {
            comment.Property(c => c.Body).HasMaxLength(1000);
            comment.Property(c => c.TargetType).HasConversion<string>().HasMaxLength(20);
            comment.HasIndex(c => new { c.TargetType, c.TargetId, c.CreatedAt });
            comment.HasOne<Band>().WithMany().HasForeignKey(c => c.BandId).OnDelete(DeleteBehavior.Cascade);
            comment.HasOne<Membership>().WithMany().HasForeignKey(c => c.AuthorMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            comment.HasQueryFilter(c => c.BandId == _bandContext.BandId);
        });

        builder.Entity<Channel>(channel =>
        {
            channel.Property(c => c.Name).HasMaxLength(50);
            channel.HasOne<Band>().WithMany().HasForeignKey(c => c.BandId).OnDelete(DeleteBehavior.Cascade);
            channel.HasQueryFilter(c => c.BandId == _bandContext.BandId);
        });

        builder.Entity<Message>(message =>
        {
            message.Property(m => m.Body).HasMaxLength(2000);
            message.HasIndex(m => new { m.ChannelId, m.CreatedAt });
            message.HasOne<Channel>().WithMany().HasForeignKey(m => m.ChannelId).OnDelete(DeleteBehavior.Cascade);
            message.HasOne<Membership>().WithMany().HasForeignKey(m => m.AuthorMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            message.HasQueryFilter(m => m.BandId == _bandContext.BandId);
        });

        builder.Entity<Stem>(stem =>
        {
            stem.Property(s => s.FileKey).HasMaxLength(300);
            stem.Property(s => s.FileName).HasMaxLength(200);
            stem.Property(s => s.ContentType).HasMaxLength(100);
            stem.Property(s => s.Name).HasMaxLength(50);
            stem.Property(s => s.Label).HasConversion<string>().HasMaxLength(20);
            stem.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            stem.HasOne<SongIdea>().WithMany().HasForeignKey(s => s.SongIdeaId).OnDelete(DeleteBehavior.Cascade);
            stem.HasOne<Membership>().WithMany().HasForeignKey(s => s.UploadedByMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            stem.HasQueryFilter(s => s.BandId == _bandContext.BandId);
        });

        builder.Entity<PolishJob>(job =>
        {
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
            job.Property(j => j.Error).HasMaxLength(500);
            job.HasOne<SongIdea>().WithMany().HasForeignKey(j => j.SongIdeaId).OnDelete(DeleteBehavior.Cascade);
            job.HasOne<Membership>().WithMany().HasForeignKey(j => j.RequestedByMembershipId)
                .OnDelete(DeleteBehavior.Cascade);
            job.HasQueryFilter(j => j.BandId == _bandContext.BandId);
        });
    }
}
