using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder routes)
    {
        var events = routes.MapGroup("/bands/{bandId:guid}/events")
            .RequireAuthorization()
            .WithTags("events")
            .AddEndpointFilter<BandMemberFilter>();

        events.MapPost("/", CreateAsync).WithSummary("Create a practice/gig/deadline (proposed by default)");
        events.MapGet("/", ListAsync).WithSummary("Upcoming events");
        events.MapGet("/{eventId:guid}", DetailAsync).WithSummary("Event detail + rsvps");
        events.MapPost("/{eventId:guid}/rsvp", RsvpAsync).WithSummary("Set my rsvp (confirms the event at quorum)");
        events.MapPost("/{eventId:guid}/confirm", ConfirmAsync).WithSummary("Confirm a proposed event (creator/admin)");
        events.MapPost("/{eventId:guid}/cancel", CancelAsync).WithSummary("Cancel an event (creator/admin)");

        return routes;
    }

    private static async Task<Results<Created<EventDetailResponse>, ValidationProblem>> CreateAsync(
        CreateEventRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var band = bandContext.Band;
        var today = BandTime.TodayIn(band, clock.GetCurrentInstant());

        var errors = new Dictionary<string, string[]>();
        if (request.Date < today.PlusDays(-366) || request.Date > today.PlusDays(731))
        {
            errors["date"] = ["Date must be within a year back and two years ahead."];
        }

        if (request.StartTime is { } start && request.EndTime is { } end && end <= start)
        {
            errors["endTime"] = ["End must be after start (overnight events are not supported yet)."];
        }

        if (request.Title is { Length: > 120 })
        {
            errors["title"] = ["Title must be at most 120 characters."];
        }

        if (request.Location is { Length: > 200 })
        {
            errors["location"] = ["Location must be at most 200 characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = clock.GetCurrentInstant();
        var evt = new Event
        {
            BandId = bandContext.BandId!.Value,
            Type = request.Type,
            Status = request.Confirmed ? EventStatus.Confirmed : EventStatus.Proposed,
            Date = request.Date,
            // A practice rides a slot unless precise times are given.
            Slot = request.Slot ?? (request.Type == EventType.Practice && request.StartTime is null
                ? band.DefaultPracticeSlot
                : null),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Title = request.Title?.Trim(),
            Location = request.Location?.Trim(),
            CreatedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = now,
        };
        db.Events.Add(evt);

        // Proposing implies attending — the creator's rsvp seeds the quorum count.
        db.Rsvps.Add(new Rsvp
        {
            BandId = evt.BandId,
            EventId = evt.Id,
            MembershipId = bandContext.MembershipId!.Value,
            Status = RsvpStatus.Going,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await notifier.EventChangedAsync(evt.BandId, evt.Id, "created", ct);

        return TypedResults.Created(
            $"/bands/{evt.BandId}/events/{evt.Id}", await LoadDetailAsync(db, evt.Id, ct));
    }

    private static async Task<Results<Ok<List<EventSummaryResponse>>, ValidationProblem>> ListAsync(
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct,
        int days = 60)
    {
        if (days is < 1 or > 366)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["days"] = ["Days must be between 1 and 366."],
            });
        }

        var band = bandContext.Band;
        var from = BandTime.TodayIn(band, clock.GetCurrentInstant());
        var to = from.PlusDays(days - 1);

        var events = await db.Events
            .Where(e => e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.CreatedAt)
            .Select(e => new EventSummaryResponse(
                e.Id, e.Type, e.Status, e.Date, e.Slot, e.StartTime, e.EndTime, e.Title, e.Location,
                db.Rsvps.Count(r => r.EventId == e.Id && r.Status == RsvpStatus.Going)))
            .ToListAsync(ct);
        return TypedResults.Ok(events);
    }

    private static async Task<Results<Ok<EventDetailResponse>, NotFound>> DetailAsync(
        Guid eventId,
        AppDbContext db,
        CancellationToken ct)
    {
        var exists = await db.Events.AnyAsync(e => e.Id == eventId, ct);
        return exists ? TypedResults.Ok(await LoadDetailAsync(db, eventId, ct)) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem>> RsvpAsync(
        Guid eventId,
        RsvpRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        CancellationToken ct)
    {
        if (request.Note is { Length: > 200 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["note"] = ["Note must be at most 200 characters."],
            });
        }

        var evt = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (evt is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetCurrentInstant();
        var rsvp = await db.Rsvps.SingleOrDefaultAsync(
            r => r.EventId == eventId && r.MembershipId == bandContext.MembershipId, ct);
        if (rsvp is null)
        {
            db.Rsvps.Add(new Rsvp
            {
                BandId = evt.BandId,
                EventId = eventId,
                MembershipId = bandContext.MembershipId!.Value,
                Status = request.Status,
                Note = request.Note?.Trim(),
                UpdatedAt = now,
            });
        }
        else
        {
            rsvp.Status = request.Status;
            rsvp.Note = request.Note?.Trim();
            rsvp.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        var action = "rsvp";
        if (evt.Status == EventStatus.Proposed)
        {
            // Confirmation beats calculation (spec principle 2): quorum of real
            // yeses — clamped to the member count — flips the proposal.
            var band = bandContext.Band;
            var memberCount = await db.Memberships.CountAsync(ct);
            var going = await db.Rsvps.CountAsync(
                r => r.EventId == eventId && r.Status == RsvpStatus.Going, ct);
            if (going >= Math.Max(1, Math.Min(band.Quorum, memberCount)))
            {
                evt.Status = EventStatus.Confirmed;
                await db.SaveChangesAsync(ct);
                action = "confirmed";
            }
        }

        await notifier.EventChangedAsync(evt.BandId, evt.Id, action, ct);
        return TypedResults.Ok(await LoadDetailAsync(db, eventId, ct));
    }

    private static Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem, ForbidHttpResult>> ConfirmAsync(
        Guid eventId,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        CancellationToken ct) =>
        TransitionAsync(eventId, bandContext, db, notifier, EventStatus.Confirmed, "confirmed", ct);

    private static Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem, ForbidHttpResult>> CancelAsync(
        Guid eventId,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        CancellationToken ct) =>
        TransitionAsync(eventId, bandContext, db, notifier, EventStatus.Cancelled, "cancelled", ct);

    private static async Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem, ForbidHttpResult>> TransitionAsync(
        Guid eventId,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        EventStatus target,
        string action,
        CancellationToken ct)
    {
        var evt = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (evt is null)
        {
            return TypedResults.NotFound();
        }

        var mayManage = bandContext.Role == BandRole.Admin ||
                        evt.CreatedByMembershipId == bandContext.MembershipId;
        if (!mayManage)
        {
            return TypedResults.Forbid();
        }

        if (target == EventStatus.Confirmed && evt.Status != EventStatus.Proposed)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["Only proposed events can be confirmed."],
            });
        }

        if (evt.Status != target)
        {
            evt.Status = target;
            await db.SaveChangesAsync(ct);
            await notifier.EventChangedAsync(evt.BandId, evt.Id, action, ct);
        }

        return TypedResults.Ok(await LoadDetailAsync(db, eventId, ct));
    }

    private static async Task<EventDetailResponse> LoadDetailAsync(AppDbContext db, Guid eventId, CancellationToken ct)
    {
        var evt = await db.Events.SingleAsync(e => e.Id == eventId, ct);
        var memberNames = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Id, u.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var rsvpRows = await db.Rsvps
            .Where(r => r.EventId == eventId)
            .OrderBy(r => r.UpdatedAt)
            .ToListAsync(ct);

        var going = rsvpRows.Count(r => r.Status == RsvpStatus.Going);
        return new EventDetailResponse(
            new EventSummaryResponse(
                evt.Id, evt.Type, evt.Status, evt.Date, evt.Slot, evt.StartTime, evt.EndTime,
                evt.Title, evt.Location, going),
            rsvpRows
                .Select(r => new RsvpResponse(
                    r.MembershipId,
                    memberNames.GetValueOrDefault(r.MembershipId, "?"),
                    r.Status,
                    r.Note))
                .ToList());
    }
}
