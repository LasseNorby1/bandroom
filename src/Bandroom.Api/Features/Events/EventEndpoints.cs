using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Features.Push;
using Bandroom.Api.Infrastructure.Email;
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
        events.MapPatch("/{eventId:guid}", UpdateAsync).WithSummary("Edit title, location, contact (creator/admin)");
        events.MapPost("/{eventId:guid}/items", AddItemAsync).WithSummary("Who brings what: add a line");
        events.MapPatch("/{eventId:guid}/items/{itemId:guid}", UpdateItemAsync).WithSummary("Who brings what: claim, tick, reword");
        events.MapDelete("/{eventId:guid}/items/{itemId:guid}", RemoveItemAsync).WithSummary("Who brings what: remove a line");
        events.MapPut("/{eventId:guid}/setlist", ReplaceSetlistAsync).WithSummary("Replace the setlist (ordered song ids)");

        return routes;
    }

    private static async Task<Results<Created<EventDetailResponse>, ValidationProblem>> CreateAsync(
        CreateEventRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        IPushSender push,
        AuthOptions authOptions,
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

        ValidateContact(errors, request.ContactName, request.ContactPhone, request.ContactEmail);
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
            ContactName = Clean(request.ContactName),
            ContactPhone = Clean(request.ContactPhone),
            ContactEmail = Clean(request.ContactEmail),
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
        if (evt.Status == EventStatus.Proposed)
        {
            await EventPush.ProposedAsync(db, push, authOptions, band, evt, bandContext.MembershipId, now, ct);
        }

        return TypedResults.Created(
            $"/bands/{evt.BandId}/events/{evt.Id}", await LoadDetailAsync(db, evt.Id, bandContext.MembershipId!.Value, ct));
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

        var me = bandContext.MembershipId!.Value;
        var events = await db.Events
            .Where(e => e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.CreatedAt)
            .Select(e => new EventSummaryResponse(
                e.Id, e.Type, e.Status, e.Date, e.Slot, e.StartTime, e.EndTime, e.Title, e.Location,
                db.Rsvps.Count(r => r.EventId == e.Id && r.Status == RsvpStatus.Going),
                e.CreatedByMembershipId,
                e.QuorumWarnedAt != null,
                db.Rsvps.Where(r => r.EventId == e.Id && r.MembershipId == me).Select(r => (RsvpStatus?)r.Status).FirstOrDefault()))
            .ToListAsync(ct);
        return TypedResults.Ok(events);
    }

    private static async Task<Results<Ok<EventDetailResponse>, NotFound>> DetailAsync(
        Guid eventId,
        BandContext bandContext,
        AppDbContext db,
        CancellationToken ct)
    {
        var exists = await db.Events.AnyAsync(e => e.Id == eventId, ct);
        return exists ? TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct)) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem>> RsvpAsync(
        Guid eventId,
        RsvpRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        IAppEmailSender email,
        IPushSender push,
        AuthOptions authOptions,
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

        var membership = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        await RsvpService.ApplyAsync(
            db, bandContext.Band, membership, evt, request.Status, request.Note, clock.GetCurrentInstant(),
            notifier, email, push, authOptions, ct);
        return TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct));
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

        return TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct));
    }

    private static async Task<EventDetailResponse> LoadDetailAsync(
        AppDbContext db, Guid eventId, Guid membershipId, CancellationToken ct)
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
        var items = await db.EventItems
            .Where(i => i.EventId == eventId)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new EventItemResponse(
                i.Id, i.Text, i.AssigneeMembershipId,
                i.AssigneeMembershipId == null ? null : memberNames.GetValueOrDefault(i.AssigneeMembershipId.Value),
                i.Done))
            .ToListAsync(ct);
        var setlist = await db.SetlistEntries
            .Where(e => e.EventId == eventId)
            .OrderBy(e => e.Position)
            .Join(db.Songs, e => e.SongId, s => s.Id, (e, s) => new SetlistItemResponse(s.Id, s.Title, s.Key, s.Bpm, s.DurationSeconds))
            .ToListAsync(ct);
        return new EventDetailResponse(
            new EventSummaryResponse(
                evt.Id, evt.Type, evt.Status, evt.Date, evt.Slot, evt.StartTime, evt.EndTime,
                evt.Title, evt.Location, going, evt.CreatedByMembershipId, evt.QuorumWarnedAt != null,
                rsvpRows.Where(r => r.MembershipId == membershipId).Select(r => (RsvpStatus?)r.Status).FirstOrDefault()),
            rsvpRows
                .Select(r => new RsvpResponse(
                    r.MembershipId,
                    memberNames.GetValueOrDefault(r.MembershipId, "?"),
                    r.Status,
                    r.Note))
                .ToList(),
            new EventContactResponse(evt.ContactName, evt.ContactPhone, evt.ContactEmail),
            items,
            setlist,
            setlist.Sum(s => s.DurationSeconds ?? 0));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateContact(Dictionary<string, string[]> errors, string? name, string? phone, string? email)
    {
        if (name is { Length: > 100 })
        {
            errors["contactName"] = ["Contact name must be at most 100 characters."];
        }

        if (phone is { Length: > 40 })
        {
            errors["contactPhone"] = ["Phone must be at most 40 characters."];
        }

        if (email is { Length: > 200 } || (!string.IsNullOrWhiteSpace(email) && !email.Contains('@')))
        {
            errors["contactEmail"] = ["That doesn't look like an email address."];
        }
    }

    private static async Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem, ForbidHttpResult>> UpdateAsync(
        Guid eventId,
        UpdateEventRequest request,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Title is { Length: > 120 })
        {
            errors["title"] = ["Title must be at most 120 characters."];
        }

        if (request.Location is { Length: > 200 })
        {
            errors["location"] = ["Location must be at most 200 characters."];
        }

        ValidateContact(errors, request.ContactName, request.ContactPhone, request.ContactEmail);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var evt = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (evt is null)
        {
            return TypedResults.NotFound();
        }

        if (bandContext.Role != BandRole.Admin && evt.CreatedByMembershipId != bandContext.MembershipId)
        {
            return TypedResults.Forbid();
        }

        if (request.Title is not null) evt.Title = Clean(request.Title);
        if (request.Location is not null) evt.Location = Clean(request.Location);
        if (request.ContactName is not null) evt.ContactName = Clean(request.ContactName);
        if (request.ContactPhone is not null) evt.ContactPhone = Clean(request.ContactPhone);
        if (request.ContactEmail is not null) evt.ContactEmail = Clean(request.ContactEmail);
        await db.SaveChangesAsync(ct);
        await notifier.EventChangedAsync(evt.BandId, evt.Id, "updated", ct);
        return TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct));
    }

    // ---- who brings what ------------------------------------------------------

    private static async Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem>> AddItemAsync(
        Guid eventId,
        CreateEventItemRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var text = request.Text?.Trim() ?? "";
        if (text.Length is 0 or > 200)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["1–200 characters."] });
        }

        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct))
        {
            return TypedResults.NotFound();
        }

        if (request.AssigneeMembershipId is { } assignee &&
            !await db.Memberships.AnyAsync(m => m.Id == assignee && m.LeftAt == null, ct))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["assigneeMembershipId"] = ["Not a member."] });
        }

        db.EventItems.Add(new EventItem
        {
            BandId = bandContext.BandId!.Value,
            EventId = eventId,
            Text = text,
            AssigneeMembershipId = request.AssigneeMembershipId,
            CreatedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = clock.GetCurrentInstant(),
        });
        await db.SaveChangesAsync(ct);
        await notifier.EventChangedAsync(bandContext.BandId!.Value, eventId, "items", ct);
        return TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct));
    }

    private static async Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem>> UpdateItemAsync(
        Guid eventId,
        Guid itemId,
        UpdateEventItemRequest request,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var item = await db.EventItems.SingleOrDefaultAsync(i => i.Id == itemId && i.EventId == eventId, ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Text is not null)
        {
            var text = request.Text.Trim();
            if (text.Length is 0 or > 200)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["1–200 characters."] });
            }

            item.Text = text;
        }

        if (request.ClearAssignee)
        {
            item.AssigneeMembershipId = null;
        }
        else if (request.AssigneeMembershipId is { } assignee)
        {
            if (!await db.Memberships.AnyAsync(m => m.Id == assignee && m.LeftAt == null, ct))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["assigneeMembershipId"] = ["Not a member."] });
            }

            item.AssigneeMembershipId = assignee;
        }

        if (request.Done is { } done)
        {
            item.Done = done;
        }

        await db.SaveChangesAsync(ct);
        await notifier.EventChangedAsync(bandContext.BandId!.Value, eventId, "items", ct);
        return TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct));
    }

    private static async Task<Results<Ok<EventDetailResponse>, NotFound>> RemoveItemAsync(
        Guid eventId,
        Guid itemId,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var removed = await db.EventItems.Where(i => i.Id == itemId && i.EventId == eventId).ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            return TypedResults.NotFound();
        }

        await notifier.EventChangedAsync(bandContext.BandId!.Value, eventId, "items", ct);
        return TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct));
    }

    // ---- setlist ----------------------------------------------------------------

    private static async Task<Results<Ok<EventDetailResponse>, NotFound, ValidationProblem>> ReplaceSetlistAsync(
        Guid eventId,
        ReplaceSetlistRequest request,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct))
        {
            return TypedResults.NotFound();
        }

        var ids = (request.SongIds ?? []).Distinct().ToList();
        if (ids.Count > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["songIds"] = ["At most 100 songs."] });
        }

        var known = await db.Songs.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
        if (known.Count != ids.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["songIds"] = ["A song isn't in this band's repertoire."] });
        }

        // Replace wholesale: the client owns the order, the server owns the constraint.
        await db.SetlistEntries.Where(e => e.EventId == eventId).ExecuteDeleteAsync(ct);
        db.SetlistEntries.AddRange(ids.Select((songId, index) => new SetlistEntry
        {
            BandId = bandContext.BandId!.Value, EventId = eventId, SongId = songId, Position = index,
        }));
        await db.SaveChangesAsync(ct);
        await notifier.EventChangedAsync(bandContext.BandId!.Value, eventId, "setlist", ct);
        return TypedResults.Ok(await LoadDetailAsync(db, eventId, bandContext.MembershipId!.Value, ct));
    }
}
