using NodaTime;

namespace Bandroom.Domain.Scheduling;

/// <summary>
/// The core mechanic (spec §4, fig 1–2): weekly patterns + blockouts overlay into
/// per-day availability, and viable days come back ranked. Pure by design — no
/// clock, no I/O — so it stays trivially testable and its answers stay explainable
/// ("4/4 free, soonest"). Dates are band-local <see cref="LocalDate"/>s; concrete
/// times and timezones are applied only when an event is materialised.
/// </summary>
public static class PracticeFinder
{
    public static IReadOnlyList<PracticeCandidate> FindCandidates(PracticeFinderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.HorizonDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Quorum);
        ArgumentOutOfRangeException.ThrowIfNegative(request.MinDaysSinceLastPractice);

        var candidates = new List<PracticeCandidate>();
        var excluded = request.ExcludedDates is { Count: > 0 }
            ? request.ExcludedDates.ToHashSet()
            : null;

        for (var offset = 0; offset < request.HorizonDays; offset++)
        {
            var date = request.From.PlusDays(offset);

            if (excluded is not null && excluded.Contains(date))
            {
                continue;
            }

            if (request.LastPractice is { } lastPractice &&
                Period.Between(lastPractice, date, PeriodUnits.Days).Days < request.MinDaysSinceLastPractice)
            {
                continue;
            }

            var available = request.Members
                .Where(member => member.IsAvailable(date, request.Slot))
                .Select(member => member.MembershipId)
                .ToList();

            if (available.Count >= request.Quorum)
            {
                candidates.Add(new PracticeCandidate(date, request.Slot, available, request.Members.Count));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.AvailableCount)
            .ThenBy(candidate => candidate.Date)
            .ToList();
    }
}
