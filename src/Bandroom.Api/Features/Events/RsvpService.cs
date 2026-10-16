using Bandroom.Api.Data;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Features.Push;
using Bandroom.Api.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Events;

/// <summary>
/// The one place an rsvp is applied — from the app and from a notification
/// tap alike. Works without a band context (the anonymous token route), so
/// every query opts out of the filters and carries the band id explicitly.
/// </summary>
public static class RsvpService
{
    public static async Task<EventSummaryResponse> ApplyAsync(
        AppDbContext db,
        Band band,
        Membership membership,
        Event evt,
        RsvpStatus status,
        string? note,
        Instant now,
        BandNotifier notifier,
        IAppEmailSender email,
        IPushSender push,
        AuthOptions authOptions,
        CancellationToken ct)
    {
        var rsvp = await db.Rsvps.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.BandId == band.Id && r.EventId == evt.Id && r.MembershipId == membership.Id, ct);
        if (rsvp is null)
        {
            db.Rsvps.Add(new Rsvp
            {
                BandId = band.Id,
                EventId = evt.Id,
                MembershipId = membership.Id,
                Status = status,
                Note = note?.Trim(),
                UpdatedAt = now,
            });
        }
        else
        {
            rsvp.Status = status;
            rsvp.Note = note?.Trim();
            rsvp.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        var action = "rsvp";
        var memberCount = await db.Memberships.IgnoreQueryFilters().CountAsync(m => m.BandId == band.Id && m.LeftAt == null, ct);
        var quorum = Math.Max(1, Math.Min(band.Quorum, memberCount));
        var going = await db.Rsvps.IgnoreQueryFilters().CountAsync(r => r.EventId == evt.Id && r.Status == RsvpStatus.Going, ct);
        var summary = EventText.Summarize(evt, band);
        var path = $"/b/{band.Id}/events/{evt.Id}";

        if (evt.Status == EventStatus.Proposed)
        {
            // Confirmation beats calculation (spec principle 2): quorum of real
            // yeses — clamped to the member count — flips the proposal.
            if (going >= quorum)
            {
                evt.Status = EventStatus.Confirmed;
                await db.SaveChangesAsync(ct);
                action = "confirmed";
                foreach (var userId in await EventPush.MemberUserIdsAsync(db, band.Id, except: null, ct))
                {
                    push.Send(userId, new PushPayload(
                        $"{summary} is on",
                        $"{EventText.When(evt, band)} — {going} of {memberCount} going.",
                        path, $"event-{evt.Id}"));
                }
            }
        }
        else if (evt.Status == EventStatus.Confirmed)
        {
            // A late "can't make it" that drops a confirmed practice below quorum
            // asks the organiser once whether to keep or cancel (spec §4). It
            // re-arms if the count recovers and drops again.
            if (going < quorum && evt.QuorumWarnedAt is null)
            {
                evt.QuorumWarnedAt = now;
                await db.SaveChangesAsync(ct);
                action = "belowQuorum";

                var organiser = await db.Memberships.IgnoreQueryFilters()
                    .Where(m => m.Id == evt.CreatedByMembershipId && m.LeftAt == null)
                    .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email, u.DisplayName })
                    .SingleOrDefaultAsync(ct);
                if (organiser is not null)
                {
                    var body = $"{going} of {quorum} are now going to {summary} on {evt.Date}.";
                    push.Send(organiser.Id, new PushPayload("Below quorum", body + " Keep it or cancel?", path, $"quorum-{evt.Id}"));
                    if (organiser.Email is not null)
                    {
                        await email.SendAsync(
                            organiser.Email,
                            $"Below quorum: {summary}",
                            $"Hi {organiser.DisplayName} — {body} Keep it or cancel: {authOptions.WebBaseUrl}{path}",
                            ct);
                    }
                }
            }
            else if (going >= quorum && evt.QuorumWarnedAt is not null)
            {
                evt.QuorumWarnedAt = null;
                await db.SaveChangesAsync(ct);
            }
        }

        await notifier.EventChangedAsync(evt.BandId, evt.Id, action, ct);
        var mine = await db.Rsvps.IgnoreQueryFilters()
            .Where(r => r.EventId == evt.Id && r.MembershipId == membership.Id)
            .Select(r => (RsvpStatus?)r.Status)
            .FirstOrDefaultAsync(ct);
        return new EventSummaryResponse(
            evt.Id, evt.Type, evt.Status, evt.Date, evt.Slot, evt.StartTime, evt.EndTime, evt.Title, evt.Location,
            going, evt.CreatedByMembershipId, evt.QuorumWarnedAt != null, mine);
    }
}

/// <summary>Push payloads for event moments. Proposals carry one-tap answers per member.</summary>
public static class EventPush
{
    public static async Task<List<Guid>> MemberUserIdsAsync(AppDbContext db, Guid bandId, Guid? except, CancellationToken ct) =>
        await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.BandId == bandId && m.LeftAt == null && (except == null || m.Id != except))
            .Select(m => m.UserId)
            .ToListAsync(ct);

    /// <summary>"Thursday works for everyone — I'm in / Can't": to every member but the proposer.</summary>
    public static async Task ProposedAsync(
        AppDbContext db, IPushSender push, AuthOptions authOptions, Band band, Event evt, Guid? proposerMembershipId, Instant now, CancellationToken ct)
    {
        var members = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.BandId == band.Id && m.LeftAt == null && (proposerMembershipId == null || m.Id != proposerMembershipId))
            .Select(m => new { m.Id, m.UserId })
            .ToListAsync(ct);
        var expires = now.Plus(Duration.FromDays(7));
        var path = $"/b/{band.Id}/events/{evt.Id}";
        foreach (var member in members)
        {
            var token = RsvpTokens.Create(authOptions.JwtSigningKey, member.Id, evt.Id, expires);
            push.Send(member.UserId, new PushPayload(
                $"{EventText.Summarize(evt, band)} proposed",
                $"{EventText.When(evt, band)}{(evt.Location is null && band.RehearsalSpace is null ? "" : $" · {evt.Location ?? band.RehearsalSpace}")}. Are you in?",
                path,
                $"event-{evt.Id}",
                [
                    new PushAction("going", "I'm in", $"/push/rsvp?token={token}&status=going"),
                    new PushAction("notGoing", "Can't", $"/push/rsvp?token={token}&status=notGoing"),
                ]));
        }
    }
}
