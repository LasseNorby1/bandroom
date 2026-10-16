using System.Net;
using NodaTime;

namespace Bandroom.Api.Tests.Support;

/// <summary>Serves one ics for any url: an all-day event in 3 days and an evening event in 5 days (Copenhagen).</summary>
public sealed class FakeCalendarHandler : HttpMessageHandler
{
    public static LocalDate Today =>
        SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/Copenhagen"]).Date;

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (request.RequestUri!.AbsolutePath.Contains("missing"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var allDay = Today.PlusDays(3);
        var evening = Today.PlusDays(5);
        var ics = $"""
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//test//EN
            BEGIN:VEVENT
            UID:allday@test
            DTSTART;VALUE=DATE:{allDay:yyyyMMdd}
            DTEND;VALUE=DATE:{allDay.PlusDays(1):yyyyMMdd}
            SUMMARY:Roskilde
            END:VEVENT
            BEGIN:VEVENT
            UID:evening@test
            DTSTART;TZID=Europe/Copenhagen:{evening:yyyyMMdd}T193000
            DTEND;TZID=Europe/Copenhagen:{evening:yyyyMMdd}T210000
            SUMMARY:Dinner
            END:VEVENT
            END:VCALENDAR
            """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ics, System.Text.Encoding.UTF8, "text/calendar"),
        });
    }
}
