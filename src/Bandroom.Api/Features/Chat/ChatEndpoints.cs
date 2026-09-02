using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace Bandroom.Api.Features.Chat;

public sealed record ChannelResponse(Guid Id, string Name, bool IsDefault, Instant CreatedAt);

public sealed record CreateChannelRequest(string Name);

public sealed record SendMessageRequest(string Body);

public sealed record MessageResponse(
    Guid Id, Guid AuthorMembershipId, string AuthorName, string Body, Instant CreatedAt);

public sealed record MessagesPage(IReadOnlyList<MessageResponse> Items, string? NextBefore);

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder routes)
    {
        var channels = routes.MapGroup("/bands/{bandId:guid}/channels")
            .RequireAuthorization()
            .WithTags("chat")
            .AddEndpointFilter<BandMemberFilter>();

        channels.MapGet("/", ListChannelsAsync).WithSummary("Channels (#general always exists)");
        channels.MapPost("/", CreateChannelAsync).WithSummary("Create a channel");
        channels.MapGet("/{channelId:guid}/messages", ListMessagesAsync).WithSummary("Messages, newest page first");
        channels.MapPost("/{channelId:guid}/messages", SendMessageAsync).WithSummary("Send a message");

        return routes;
    }

    private static async Task<Ok<List<ChannelResponse>>> ListChannelsAsync(
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        // Bands created before chat shipped get their #general lazily.
        if (!await db.Channels.AnyAsync(ct))
        {
            db.Channels.Add(new Channel
            {
                BandId = bandContext.BandId!.Value,
                Name = "general",
                IsDefault = true,
                CreatedAt = clock.GetCurrentInstant(),
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two first-loads raced; one #general won.
            }
        }

        var channels = await db.Channels
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.CreatedAt)
            .Select(c => new ChannelResponse(c.Id, c.Name, c.IsDefault, c.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(channels);
    }

    private static async Task<Results<Created<ChannelResponse>, ValidationProblem>> CreateChannelAsync(
        CreateChannelRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var name = request.Name?.Trim().TrimStart('#') ?? "";
        if (name.Length is 0 or > 50)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Channel name must be 1–50 characters."],
            });
        }

        var channel = new Channel
        {
            BandId = bandContext.BandId!.Value,
            Name = name,
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created(
            $"/bands/{channel.BandId}/channels/{channel.Id}",
            new ChannelResponse(channel.Id, channel.Name, channel.IsDefault, channel.CreatedAt));
    }

    private static async Task<Results<Ok<MessagesPage>, NotFound, ValidationProblem>> ListMessagesAsync(
        Guid channelId,
        AppDbContext db,
        CancellationToken ct,
        string? before = null,
        int take = 50)
    {
        if (take is < 1 or > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["take"] = ["Take must be between 1 and 100."],
            });
        }

        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
        {
            return TypedResults.NotFound();
        }

        Instant? beforeInstant = null;
        if (before is not null)
        {
            var parsed = InstantPattern.ExtendedIso.Parse(before);
            if (!parsed.Success)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["before"] = ["Cursor must be an iso-8601 instant."],
                });
            }

            beforeInstant = parsed.Value;
        }

        var query = db.Messages.Where(m => m.ChannelId == channelId);
        if (beforeInstant is { } cursor)
        {
            query = query.Where(m => m.CreatedAt < cursor);
        }

        var page = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .Join(db.Memberships, m => m.AuthorMembershipId, mem => mem.Id, (m, mem) => new { m, mem })
            .Join(db.Users, x => x.mem.UserId, u => u.Id, (x, u) => new { x.m, u.DisplayName })
            .ToListAsync(ct);

        var items = page
            .OrderBy(x => x.m.CreatedAt)
            .Select(x => new MessageResponse(x.m.Id, x.m.AuthorMembershipId, x.DisplayName, x.m.Body, x.m.CreatedAt))
            .ToList();
        var nextBefore = page.Count == take
            ? InstantPattern.ExtendedIso.Format(page.Min(x => x.m.CreatedAt))
            : null;
        return TypedResults.Ok(new MessagesPage(items, nextBefore));
    }

    private static async Task<Results<Created<MessageResponse>, NotFound, ValidationProblem>> SendMessageAsync(
        Guid channelId,
        SendMessageRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var body = request.Body?.Trim() ?? "";
        if (body.Length is 0 or > 2000)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["Message must be 1–2000 characters."],
            });
        }

        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
        {
            return TypedResults.NotFound();
        }

        var message = new Message
        {
            BandId = bandContext.BandId!.Value,
            ChannelId = channelId,
            AuthorMembershipId = bandContext.MembershipId!.Value,
            Body = body,
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        await notifier.MessageAddedAsync(message.BandId, channelId, ct);

        var authorName = await db.Memberships
            .Where(m => m.Id == message.AuthorMembershipId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.DisplayName)
            .SingleAsync(ct);
        return TypedResults.Created(
            $"/bands/{message.BandId}/channels/{channelId}/messages/{message.Id}",
            new MessageResponse(message.Id, message.AuthorMembershipId, authorName, message.Body, message.CreatedAt));
    }
}
