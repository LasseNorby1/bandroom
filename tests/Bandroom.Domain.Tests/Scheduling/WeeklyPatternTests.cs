using Bandroom.Domain.Scheduling;
using NodaTime;
using Xunit;

namespace Bandroom.Domain.Tests.Scheduling;

public class WeeklyPatternTests
{
    private static readonly (IsoDayOfWeek, PracticeSlot)[] ThursdayEvening = [(IsoDayOfWeek.Thursday, PracticeSlot.Evening)];

    [Fact]
    public void Alternating_SwitchesEveryWeek_FromTheAnchor()
    {
        // Anchor: Monday 2026-10-12 — a "without the kids" week with Thursday open.
        var anchor = new LocalDate(2026, 10, 12);
        var pattern = new WeeklyPattern(ThursdayEvening, [], anchor);

        Assert.True(pattern.IsOpen(new LocalDate(2026, 10, 15), PracticeSlot.Evening));  // thu, anchor week
        Assert.False(pattern.IsOpen(new LocalDate(2026, 10, 22), PracticeSlot.Evening)); // thu, kids week
        Assert.True(pattern.IsOpen(new LocalDate(2026, 10, 29), PracticeSlot.Evening));  // thu, two weeks on
        Assert.True(pattern.IsOpen(new LocalDate(2026, 10, 18), PracticeSlot.Evening) == false); // sunday never open
        // Before the anchor the rhythm holds too.
        Assert.False(pattern.IsOpen(new LocalDate(2026, 10, 8), PracticeSlot.Evening));
        Assert.True(pattern.IsOpen(new LocalDate(2026, 10, 1), PracticeSlot.Evening));
    }

    [Fact]
    public void Alternating_IgnoresIsoWeekNumbers_AcrossA53WeekYear()
    {
        // 2026 has 53 iso weeks; week 53 starts Monday 2026-12-28 and week 1 of 2027 on 2027-01-04.
        // Parity by week number would put both on "odd"; the anchor rhythm must not.
        var pattern = new WeeklyPattern(ThursdayEvening, [], new LocalDate(2026, 12, 28));
        Assert.True(pattern.IsOpen(new LocalDate(2026, 12, 31), PracticeSlot.Evening));
        Assert.False(pattern.IsOpen(new LocalDate(2027, 1, 7), PracticeSlot.Evening));
        Assert.True(pattern.IsOpen(new LocalDate(2027, 1, 14), PracticeSlot.Evening));
    }

    [Fact]
    public void Alternating_AnchorMustBeAMonday()
    {
        Assert.Throws<ArgumentException>(() => new WeeklyPattern(ThursdayEvening, [], new LocalDate(2026, 10, 13)));
    }

    [Fact]
    public void SameEveryWeek_IsUnaffected()
    {
        var pattern = WeeklyPattern.Evenings(IsoDayOfWeek.Thursday);
        Assert.False(pattern.Alternates);
        Assert.True(pattern.IsOpen(new LocalDate(2026, 10, 15), PracticeSlot.Evening));
        Assert.True(pattern.IsOpen(new LocalDate(2026, 10, 22), PracticeSlot.Evening));
    }
}
