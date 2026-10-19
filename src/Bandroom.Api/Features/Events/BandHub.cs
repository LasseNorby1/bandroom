using System.Security.Claims;
using Bandroom.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Bandroom.Api.Features.Events;

/// <summary>
/// One hub, one group per band (spec §6). Server → client notifications only;
/// all writes stay REST. Phase-3 chat rides this same hub later.
/// </summary>
[Authorize]
public sealed class BandHub(AppDbContext db, BandPresence presence) : Hub
{
    public static string GroupName(Guid bandId) => $"band:{bandId}";

    public async Task JoinBand(Guid bandId)
    {
        var subject = Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            throw new HubException("Unauthenticated.");
        }

        var isMember = await db.Memberships
            .IgnoreQueryFilters()
            .AnyAsync(m => m.BandId == bandId && m.UserId == userId && m.LeftAt == null);
        if (!isMember)
        {
            // Same posture as HTTP: for outsiders the band does not exist.
            throw new HubException("Unknown band.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(bandId));
        if (presence.Join(Context.ConnectionId, bandId, userId))
        {
            await BroadcastPresenceAsync(bandId);
        }
    }

    /// <summary>Who has this band open right now — for the chat's "in the room" line, nothing else.</summary>
    public IReadOnlyList<Guid> WhoIsHere(Guid bandId) =>
        presence.IsIn(Context.ConnectionId, bandId) ? presence.UserIdsIn(bandId) : [];

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var bandId in presence.Leave(Context.ConnectionId))
        {
            await BroadcastPresenceAsync(bandId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastPresenceAsync(Guid bandId) =>
        Clients.Group(GroupName(bandId)).SendAsync("presenceChanged", new PresenceChangedNotification(bandId, presence.UserIdsIn(bandId)));
}

/// <summary>
/// Connections per band, in memory. A single api instance is the deployment
/// (spec §6); a redis backplane would move this into redis along with the groups.
/// </summary>
public sealed class BandPresence
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<(Guid BandId, Guid UserId)>> _byConnection = new();
    private readonly Dictionary<Guid, Dictionary<Guid, int>> _byBand = new();

    /// <returns>True when the user wasn't in the band's room before (i.e. the member list changed).</returns>
    public bool Join(string connectionId, Guid bandId, Guid userId)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var bands))
            {
                _byConnection[connectionId] = bands = [];
            }

            if (!bands.Add((bandId, userId)))
            {
                return false;
            }

            if (!_byBand.TryGetValue(bandId, out var users))
            {
                _byBand[bandId] = users = new Dictionary<Guid, int>();
            }

            users[userId] = users.GetValueOrDefault(userId) + 1;
            return users[userId] == 1;
        }
    }

    /// <returns>The bands whose member list changed because this connection went away.</returns>
    public IReadOnlyList<Guid> Leave(string connectionId)
    {
        lock (_gate)
        {
            if (!_byConnection.Remove(connectionId, out var bands))
            {
                return [];
            }

            var changed = new List<Guid>();
            foreach (var (bandId, userId) in bands)
            {
                if (!_byBand.TryGetValue(bandId, out var users))
                {
                    continue;
                }

                var left = users.GetValueOrDefault(userId) - 1;
                if (left <= 0)
                {
                    users.Remove(userId);
                    changed.Add(bandId);
                }
                else
                {
                    users[userId] = left;
                }

                if (users.Count == 0)
                {
                    _byBand.Remove(bandId);
                }
            }

            return changed;
        }
    }

    public bool IsIn(string connectionId, Guid bandId)
    {
        lock (_gate)
        {
            return _byConnection.TryGetValue(connectionId, out var bands) && bands.Any(b => b.BandId == bandId);
        }
    }

    public IReadOnlyList<Guid> UserIdsIn(Guid bandId)
    {
        lock (_gate)
        {
            return _byBand.TryGetValue(bandId, out var users) ? [.. users.Keys] : [];
        }
    }
}

/// <summary>Fire-and-forget change signals; clients refetch what they care about.</summary>
public sealed class BandNotifier(IHubContext<BandHub> hub)
{
    public Task EventChangedAsync(Guid bandId, Guid eventId, string action, CancellationToken ct = default) =>
        hub.Clients.Group(BandHub.GroupName(bandId))
            .SendAsync("eventChanged", new EventChangedNotification(bandId, eventId, action), ct);

    public Task DemoChangedAsync(Guid bandId, Guid songIdeaId, string action, CancellationToken ct = default) =>
        hub.Clients.Group(BandHub.GroupName(bandId))
            .SendAsync("demoChanged", new DemoChangedNotification(bandId, songIdeaId, action), ct);

    public Task MessageAddedAsync(Guid bandId, Guid channelId, CancellationToken ct = default) =>
        hub.Clients.Group(BandHub.GroupName(bandId))
            .SendAsync("messageAdded", new MessageAddedNotification(bandId, channelId), ct);
}

public sealed record EventChangedNotification(Guid BandId, Guid EventId, string Action);

public sealed record DemoChangedNotification(Guid BandId, Guid SongId, string Action);

public sealed record MessageAddedNotification(Guid BandId, Guid ChannelId);

public sealed record PresenceChangedNotification(Guid BandId, IReadOnlyList<Guid> UserIds);
