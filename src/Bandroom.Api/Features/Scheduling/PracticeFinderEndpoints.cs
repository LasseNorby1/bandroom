using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Infrastructure;
using Bandroom.Domain.Scheduling;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Scheduling;

public static class PracticeFinderEndpoints
{
    public static IEndpointRouteBuilder MapPracticeFinderEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGroup("/bands/{bandId:guid}")
            .RequireAuthorization()
            .WithTags("practice-finder")
            .AddEndpointFilter<BandMemberFilter>()
            .MapGet("/practice-finder", FindAsync)
            .WithSummary("Ranked candidate practice days from patterns + blockouts");

        return routes;
    }

    private static async Task<Results<Ok<PracticeFinderResponse>, ValidationProblem>> FindAsync(
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct,
        int days = 21,
        string? slot = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (days is < 1 or > 60)
        {
            errors["days"] = ["Days must be between 1 and 60."];
        }

        PracticeSlot? requestedSlot = null;
        if (slot is not null)
        {
            if (Enum.TryParse<PracticeSlot>(slot, ignoreCase: true, out var parsed))
            {
                requestedSlot = parsed;
            }
            else
            {
                errors["slot"] = ["Slot must be 'afternoon' or 'evening'."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var band = bandContext.Band;
        var practiceSlot = requestedSlot ?? band.DefaultPracticeSlot;

        // The search starts tomorrow, band-local — today's slot may already be under way.
        var from = BandTime.TodayIn(band, clock.GetCurrentInstant()).PlusDays(1);
        var to = from.PlusDays(days - 1);

        var members = await db.Memberships
            .Where(m => m.LeftAt == null)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Id, u.DisplayName, m.WeeklyPattern, m.AlternatePattern, m.PatternAnchor, m.PatternUpdatedAt })
            .OrderBy(m => m.DisplayName)
            .ToListAsync(ct);
        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.To >= from && e.From <= to)
            .ToListAsync(ct);

        // The calendar overlay: a day the member's own calendar fills in this slot
        // (or all day) counts like a blockout. Opt-in, read-only, never written back.
        var imported = await db.ImportedBusyDays
            .Where(b => b.Date >= from && b.Date <= to && (b.Slot == null || b.Slot == practiceSlot))
            .ToListAsync(ct);

        var lastPractice = await LatestConfirmedPracticeAsync(db, from, ct);

        // Days already holding a practice (proposed or confirmed) are out — the
        // finder proposes new days, it doesn't double-book existing ones.
        var occupiedDates = await db.Events
            .Where(e => e.Type == EventType.Practice &&
                        e.Status != EventStatus.Cancelled &&
                        e.Date >= from && e.Date <= to)
            .Select(e => e.Date)
            .ToListAsync(ct);

        var domainMembers = members
            .Select(member => new MemberAvailability(
                member.Id,
                member.PatternAnchor is { } anchor
                    ? new WeeklyPattern(
                        member.WeeklyPattern.Select(s => (s.Day, s.Slot)),
                        member.AlternatePattern.Select(s => (s.Day, s.Slot)),
                        anchor)
                    : new WeeklyPattern(member.WeeklyPattern.Select(s => (s.Day, s.Slot))),
                exceptions
                    .Where(e => e.MembershipId == member.Id)
                    .Select(e => new DateInterval(e.From, e.To))
                    .Concat(imported
                        .Where(b => b.MembershipId == member.Id)
                        .Select(b => new DateInterval(b.Date, b.Date)))
                    .ToList()))
            .ToList();

        // A quorum above the member count would make every day impossible — clamp
        // to "the whole band".
        var quorum = Math.Max(1, Math.Min(band.Quorum, domainMembers.Count));

        var candidates = PracticeFinder.FindCandidates(new PracticeFinderRequest(
            domainMembers, practiceSlot, from, days, quorum, lastPractice,
            ExcludedDates: occupiedDates));

        var names = members.ToDictionary(member => member.Id, member => member.DisplayName);
        var response = new PracticeFinderResponse(from, days, quorum, practiceSlot, candidates
            .Select(candidate => new FinderCandidateResponse(
                candidate.Date,
                candidate.Slot,
                candidate.AvailableCount,
                candidate.TotalMembers,
                candidate.EveryoneAvailable,
                candidate.AvailableMembershipIds
                    .Select(id => new FinderMemberDto(id, names[id]))
                    .ToList()))
            .ToList(),
            members
                .Select(m => new FinderMemberStatusDto(
                    m.Id, m.DisplayName, m.WeeklyPattern.Count + (m.PatternAnchor is null ? 0 : m.AlternatePattern.Count), m.PatternUpdatedAt))
                .ToList());
        return TypedResults.Ok(response);
    }

    /// <summary>Spacing input: the most recent confirmed practice (band-scoped by the query filter).</summary>
    private static async Task<LocalDate?> LatestConfirmedPracticeAsync(
        AppDbContext db, LocalDate before, CancellationToken ct) =>
        await db.Events
            .Where(e => e.Type == EventType.Practice && e.Status == EventStatus.Confirmed && e.Date < before)
            .OrderByDescending(e => e.Date)
            .Select(e => (LocalDate?)e.Date)
            .FirstOrDefaultAsync(ct);
}
