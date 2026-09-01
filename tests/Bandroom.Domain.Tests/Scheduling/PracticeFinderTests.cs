using Bandroom.Domain.Scheduling;
using NodaTime;
using Xunit;

namespace Bandroom.Domain.Tests.Scheduling;

public class PracticeFinderTests
{
    // The exact week drawn in spec fig 1: Monday 2026-09-07 .. Sunday 2026-09-13.
    private static readonly LocalDate Monday = new(2026, 9, 7);

    private static readonly Guid Lasse = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Mikkel = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Sofie = new("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Jonas = new("00000000-0000-0000-0000-000000000004");

    private static MemberAvailability Member(Guid id, WeeklyPattern pattern, params DateInterval[] blockouts) =>
        new(id, pattern, blockouts);

    private static DateInterval Day(LocalDate date) => new(date, date);

    private static IReadOnlyList<MemberAvailability> FigOneBand()
    {
        var monToSat = WeeklyPattern.Evenings(
            IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday,
            IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday);

        return
        [
            // Lasse: pattern mon–sat, blocked tuesday (travel)
            Member(Lasse, monToSat, Day(Monday.PlusDays(1))),
            // Mikkel: pattern off mondays, blocked friday
            Member(Mikkel, WeeklyPattern.Evenings(
                IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday,
                IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday), Day(Monday.PlusDays(4))),
            // Sofie: pattern off wednesdays, blocked monday
            Member(Sofie, WeeklyPattern.Evenings(
                IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Thursday,
                IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday), Day(Monday)),
            // Jonas: pattern mon–sat, blocked saturday (family)
            Member(Jonas, monToSat, Day(Monday.PlusDays(5))),
        ];
    }

    [Fact]
    public void FigOneWeek_ThursdayIsTheTopCandidate()
    {
        var candidates = PracticeFinder.FindCandidates(new PracticeFinderRequest(
            FigOneBand(), PracticeSlot.Evening, Monday, HorizonDays: 7, Quorum: 3));

        var thursday = Monday.PlusDays(3);
        Assert.Equal(thursday, candidates[0].Date);
        Assert.Equal(4, candidates[0].AvailableCount);
        Assert.True(candidates[0].EveryoneAvailable);

        // Full ranking: 4/4 thursday first, then the 3/4 days soonest-first.
        // Monday (2/4) misses quorum and sunday is outside every pattern.
        LocalDate[] expected =
        [
            thursday,
            Monday.PlusDays(1), // tue 3/4
            Monday.PlusDays(2), // wed 3/4
            Monday.PlusDays(4), // fri 3/4
            Monday.PlusDays(5), // sat 3/4
        ];
        Assert.Equal(expected, candidates.Select(candidate => candidate.Date));
    }

    [Fact]
    public void FigOneWeek_ReportsExactlyWhoIsFree()
    {
        var candidates = PracticeFinder.FindCandidates(new PracticeFinderRequest(
            FigOneBand(), PracticeSlot.Evening, Monday, HorizonDays: 7, Quorum: 3));

        var tuesday = Assert.Single(candidates, candidate => candidate.Date == Monday.PlusDays(1));
        Assert.Equal([Mikkel, Sofie, Jonas], tuesday.AvailableMembershipIds);
    }

    [Fact]
    public void BlockoutsAreInclusiveAtBothEnds()
    {
        // "Can't the 12th–15th" blocks the 15th too.
        var member = Member(
            Lasse,
            WeeklyPattern.Evenings(
                IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday,
                IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday),
            new DateInterval(new LocalDate(2026, 6, 12), new LocalDate(2026, 6, 15)));

        Assert.True(member.IsAvailable(new LocalDate(2026, 6, 11), PracticeSlot.Evening));
        Assert.False(member.IsAvailable(new LocalDate(2026, 6, 12), PracticeSlot.Evening));
        Assert.False(member.IsAvailable(new LocalDate(2026, 6, 15), PracticeSlot.Evening));
        Assert.True(member.IsAvailable(new LocalDate(2026, 6, 16), PracticeSlot.Evening));
    }

    [Fact]
    public void SlotsAreIndependent_EveningPatternDoesNotOpenAfternoons()
    {
        var eveningsOnly = Member(Lasse, WeeklyPattern.Evenings(IsoDayOfWeek.Thursday));

        var candidates = PracticeFinder.FindCandidates(new PracticeFinderRequest(
            [eveningsOnly], PracticeSlot.Afternoon, Monday, HorizonDays: 7, Quorum: 1));

        Assert.Empty(candidates);
    }

    [Fact]
    public void DaysBelowQuorumAreDropped()
    {
        var members = new[]
        {
            Member(Lasse, WeeklyPattern.Evenings(IsoDayOfWeek.Monday, IsoDayOfWeek.Thursday)),
            Member(Mikkel, WeeklyPattern.Evenings(IsoDayOfWeek.Thursday)),
        };

        var candidates = PracticeFinder.FindCandidates(new PracticeFinderRequest(
            members, PracticeSlot.Evening, Monday, HorizonDays: 7, Quorum: 2));

        var only = Assert.Single(candidates);
        Assert.Equal(Monday.PlusDays(3), only.Date);
    }

    [Fact]
    public void DaysTooCloseToTheLastPracticeAreSkipped()
    {
        var everyEvening = Member(Lasse, WeeklyPattern.Evenings(
            IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday,
            IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday));

        var candidates = PracticeFinder.FindCandidates(new PracticeFinderRequest(
            [everyEvening], PracticeSlot.Evening, Monday, HorizonDays: 7, Quorum: 1,
            LastPractice: Monday.PlusDays(-1), MinDaysSinceLastPractice: 2));

        // Sunday was practice; monday is only one day later and gets skipped.
        Assert.DoesNotContain(candidates, candidate => candidate.Date == Monday);
        Assert.Equal(Monday.PlusDays(1), candidates[0].Date);
    }

    [Fact]
    public void EmptyPatternIsNeverAvailable()
    {
        var candidates = PracticeFinder.FindCandidates(new PracticeFinderRequest(
            [Member(Lasse, WeeklyPattern.Empty)], PracticeSlot.Evening, Monday, HorizonDays: 30, Quorum: 1));

        Assert.Empty(candidates);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(7, 0)]
    public void RejectsNonPositiveHorizonOrQuorum(int horizonDays, int quorum)
    {
        var request = new PracticeFinderRequest(
            FigOneBand(), PracticeSlot.Evening, Monday, horizonDays, quorum);

        Assert.Throws<ArgumentOutOfRangeException>(() => PracticeFinder.FindCandidates(request));
    }
}
