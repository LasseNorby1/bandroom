using Bandroom.Api.Data;
using NodaTime;

namespace Bandroom.Api.Infrastructure;

/// <summary>
/// All calendar math is band-local (foundation rules, spec §6). Band.TimeZone is
/// validated at write time, so the indexer here never throws in practice.
/// </summary>
public static class BandTime
{
    public static DateTimeZone ZoneOf(Band band) => DateTimeZoneProviders.Tzdb[band.TimeZone];

    public static LocalDate TodayIn(Band band, Instant now) => now.InZone(ZoneOf(band)).Date;
}
