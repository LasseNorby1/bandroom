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
public sealed class BandHub(AppDbContext db) : Hub
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
            .AnyAsync(m => m.BandId == bandId && m.UserId == userId);
        if (!isMember)
        {
            // Same posture as HTTP: for outsiders the band does not exist.
            throw new HubException("Unknown band.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(bandId));
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

public sealed record DemoChangedNotification(Guid BandId, Guid SongIdeaId, string Action);

public sealed record MessageAddedNotification(Guid BandId, Guid ChannelId);
