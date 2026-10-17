using System.Text.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Infrastructure;
using Bandroom.Domain.Scheduling;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace Bandroom.Api.Features.Ai;

public sealed record ParseTextRequest(string Text);

public sealed record ProposedBlockout(LocalDate From, LocalDate To, string? Note);

public sealed record ParsedBlockoutsResponse(IReadOnlyList<ProposedBlockout> Blockouts, string? Unparsed);

public sealed record ParsedGigOffer(
    EventType Type,
    LocalDate? Date,
    LocalTime? StartTime,
    LocalTime? EndTime,
    string? Title,
    string? Location,
    string? Fee,
    string? Notes,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public sealed record CatchUpItem(Instant At, string Kind, string Text, string? Path);

public sealed record CatchUpResponse(Instant? Since, IReadOnlyList<CatchUpItem> Items, string? Summary);

/// <summary>
/// The roadie, never the artist (spec §5.5): ai parses, drafts and summarises;
/// a human confirms before anything is saved. Every endpoint answers 503 when
/// no key is configured and 409 when the band switched ai off.
/// </summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder routes)
    {
        var band = routes.MapGroup("/bands/{bandId:guid}")
            .RequireAuthorization()
            .WithTags("ai")
            .AddEndpointFilter<BandMemberFilter>();

        band.MapPost("/availability/me/exceptions/parse", ParseBlockoutsAsync)
            .WithSummary("Natural language → proposed blockouts (nothing saved)");
        band.MapPost("/events/parse-offer", ParseGigOfferAsync)
            .WithSummary("A pasted venue email → a draft gig event (nothing saved)");
        band.MapGet("/catch-up", CatchUpAsync).WithSummary("What happened since my last visit");
        band.MapPost("/catch-up/seen", MarkSeenAsync).WithSummary("I'm caught up");

        return routes;
    }

    private static Results<StatusCodeHttpResult, Conflict<string>, Ok<T>>? Gate<T>(BandContext bandContext, IAiClient ai)
    {
        if (!ai.IsConfigured)
        {
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!bandContext.Band.AiEnabled)
        {
            return TypedResults.Conflict("This band has switched ai off (settings).");
        }

        return null;
    }

    private static async Task<Results<StatusCodeHttpResult, Conflict<string>, Ok<ParsedBlockoutsResponse>>> ParseBlockoutsAsync(
        ParseTextRequest request,
        BandContext bandContext,
        IAiClient ai,
        IClock clock,
        CancellationToken ct)
    {
        if (Gate<ParsedBlockoutsResponse>(bandContext, ai) is { } gate)
        {
            return gate;
        }

        var band = bandContext.Band;
        var today = BandTime.TodayIn(band, clock.GetCurrentInstant());
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "blockouts", "unparsed" },
            properties = new
            {
                blockouts = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "from", "to", "note" },
                        properties = new
                        {
                            from = new { type = "string", description = "ISO date YYYY-MM-DD" },
                            to = new { type = "string", description = "ISO date YYYY-MM-DD, inclusive, >= from" },
                            note = new { type = new[] { "string", "null" }, description = "a few words, as the person said it" },
                        },
                    },
                },
                unparsed = new { type = new[] { "string", "null" }, description = "any part you could not turn into dates" },
            },
        });

        var system = $"""
            You turn a band member's casual message about when they can't make practice into blockout date ranges.
            Today is {today:yyyy-MM-dd} ({today.DayOfWeek}), timezone {band.TimeZone}. Dates without a year are the next occurrence.
            "The next two weekends" means the coming Saturday–Sunday pairs. "Tuesdays out until June" is one range per Tuesday up to and including the last Tuesday of May.
            Never invent dates the person did not mention. Keep notes short and in their words. Output only the json.
            """;

        var result = await ai.ExtractAsync(system, request.Text.Trim(), schema, ct);
        var blockouts = new List<ProposedBlockout>();
        foreach (var item in result.GetProperty("blockouts").EnumerateArray())
        {
            var from = LocalDatePattern.Iso.Parse(item.GetProperty("from").GetString() ?? "");
            var to = LocalDatePattern.Iso.Parse(item.GetProperty("to").GetString() ?? "");
            if (!from.Success || !to.Success || to.Value < from.Value || to.Value < today.PlusDays(-1))
            {
                continue;
            }

            var note = item.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            blockouts.Add(new ProposedBlockout(from.Value, to.Value, note is { Length: > 200 } ? note[..200] : note));
        }

        var unparsed = result.TryGetProperty("unparsed", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        return TypedResults.Ok(new ParsedBlockoutsResponse(blockouts.OrderBy(b => b.From).Take(60).ToList(), unparsed));
    }

    private static async Task<Results<StatusCodeHttpResult, Conflict<string>, Ok<ParsedGigOffer>>> ParseGigOfferAsync(
        ParseTextRequest request,
        BandContext bandContext,
        IAiClient ai,
        IClock clock,
        CancellationToken ct)
    {
        if (Gate<ParsedGigOffer>(bandContext, ai) is { } gate)
        {
            return gate;
        }

        var band = bandContext.Band;
        var today = BandTime.TodayIn(band, clock.GetCurrentInstant());
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "type", "date", "startTime", "endTime", "title", "location", "fee", "notes", "contactName", "contactPhone", "contactEmail" },
            properties = new
            {
                type = new { type = "string", @enum = new[] { "gig", "deadline", "other" } },
                date = new { type = new[] { "string", "null" }, description = "ISO date YYYY-MM-DD" },
                startTime = new { type = new[] { "string", "null" }, description = "HH:mm, the time the band plays or must be there" },
                endTime = new { type = new[] { "string", "null" }, description = "HH:mm" },
                title = new { type = new[] { "string", "null" }, description = "short, e.g. 'Gig at Loppen'" },
                location = new { type = new[] { "string", "null" } },
                fee = new { type = new[] { "string", "null" }, description = "money mentioned, verbatim" },
                notes = new { type = new[] { "string", "null" }, description = "load-in, set length, backline — one short line" },
                contactName = new { type = new[] { "string", "null" }, description = "the person who wrote or is named as contact" },
                contactPhone = new { type = new[] { "string", "null" }, description = "phone number verbatim" },
                contactEmail = new { type = new[] { "string", "null" } },
            },
        });
        var system = $"""
            You read a message a venue or promoter sent a band and draft one calendar event from it.
            Today is {today:yyyy-MM-dd} ({today.DayOfWeek}), timezone {band.TimeZone}. Use null for anything the message doesn't say. Never guess a fee or a date. Output only the json.
            """;

        var result = await ai.ExtractAsync(system, request.Text.Trim(), schema, ct);
        string? Str(string name) => result.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var type = Enum.TryParse<EventType>(Str("type"), ignoreCase: true, out var parsedType) ? parsedType : EventType.Gig;
        var date = LocalDatePattern.Iso.Parse(Str("date") ?? "");
        var start = LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(Str("startTime") ?? "");
        var end = LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(Str("endTime") ?? "");
        return TypedResults.Ok(new ParsedGigOffer(
            type,
            date.Success ? date.Value : null,
            start.Success ? start.Value : null,
            end.Success ? end.Value : null,
            Trim(Str("title"), 120),
            Trim(Str("location"), 200),
            Trim(Str("fee"), 60),
            Trim(Str("notes"), 300),
            Trim(Str("contactName"), 100),
            Trim(Str("contactPhone"), 40),
            Trim(Str("contactEmail"), 200)));
    }

    private static string? Trim(string? value, int max) =>
        value is null ? null : value.Length > max ? value[..max] : value;

    private static async Task<Ok<CatchUpResponse>> CatchUpAsync(
        BandContext bandContext,
        AppDbContext db,
        IAiClient ai,
        IClock clock,
        CancellationToken ct)
    {
        var band = bandContext.Band;
        var me = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        var since = me.LastSeenAt ?? me.JoinedAt;
        var names = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Id, u.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "someone");

        var items = new List<CatchUpItem>();
        var prefix = $"/b/{band.Id}";

        foreach (var e in await db.Events.Where(e => e.CreatedAt > since && e.CreatedByMembershipId != me.Id).ToListAsync(ct))
        {
            items.Add(new CatchUpItem(e.CreatedAt, "event",
                $"{Name(e.CreatedByMembershipId)} proposed {Events.EventText.Summarize(e, band)} on {e.Date}", $"{prefix}/events/{e.Id}"));
        }

        foreach (var r in await db.Rsvps.Where(r => r.UpdatedAt > since && r.MembershipId != me.Id)
                     .Join(db.Events, r => r.EventId, e => e.Id, (r, e) => new { r, e }).ToListAsync(ct))
        {
            items.Add(new CatchUpItem(r.r.UpdatedAt, "rsvp",
                $"{Name(r.r.MembershipId)} said {(r.r.Status == RsvpStatus.Going ? "yes" : r.r.Status == RsvpStatus.Maybe ? "maybe" : "no")} to {r.e.Date}{(r.r.Note is null ? "" : $" — \"{r.r.Note}\"")}",
                $"{prefix}/events/{r.e.Id}"));
        }

        foreach (var c in await db.Comments.Where(c => c.CreatedAt > since && c.AuthorMembershipId != me.Id).ToListAsync(ct))
        {
            var path = c.TargetType == CommentTarget.Event ? $"{prefix}/events/{c.TargetId}" : null;
            items.Add(new CatchUpItem(c.CreatedAt, "comment", $"{Name(c.AuthorMembershipId)}: \"{c.Body}\"", path));
        }

        foreach (var v in await db.DemoVersions.Where(v => v.CreatedAt > since && v.Status == VersionStatus.Ready && v.UploadedByMembershipId != me.Id)
                     .Join(db.SongIdeas, v => v.SongIdeaId, i => i.Id, (v, i) => new { v, i }).ToListAsync(ct))
        {
            items.Add(new CatchUpItem(v.v.CreatedAt, "demo",
                $"{Name(v.v.UploadedByMembershipId)} uploaded v{v.v.Number} of \"{v.i.Title}\"", $"{prefix}/demos/{v.i.Id}"));
        }

        var messageCount = await db.Messages.CountAsync(m => m.CreatedAt > since && m.AuthorMembershipId != me.Id, ct);
        if (messageCount > 0)
        {
            items.Add(new CatchUpItem(clock.GetCurrentInstant(), "chat", $"{messageCount} new chat message{(messageCount == 1 ? "" : "s")}", $"{prefix}/chat"));
        }

        if (band.NoticeUpdatedAt is { } noticeAt && noticeAt > since && band.NoticeByMembershipId != me.Id)
        {
            items.Add(new CatchUpItem(noticeAt, "notice", $"The noticeboard changed: \"{band.Notice}\"", prefix));
        }

        items = items.OrderByDescending(i => i.At).Take(40).ToList();

        string? summary = null;
        if (items.Count >= 3 && ai.IsConfigured && band.AiEnabled)
        {
            try
            {
                var lines = string.Join("\n", items.OrderBy(i => i.At).Select(i => $"- [{i.Kind}] {i.Text}"));
                summary = await ai.WriteAsync(
                    "You write a three-sentence catch-up for a band member who has been away, from a list of what happened in their band's room. Plain text, warm, no bullet points, no greeting. Lead with decisions and dates; mention who did what; skip anything trivial. Never invent anything not in the list.",
                    lines, ct);
            }
            catch (AiRefusedException)
            {
                summary = null;
            }
        }

        return TypedResults.Ok(new CatchUpResponse(me.LastSeenAt, items, summary));
    }

    private static async Task<NoContent> MarkSeenAsync(BandContext bandContext, AppDbContext db, IClock clock, CancellationToken ct)
    {
        var me = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        me.LastSeenAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
