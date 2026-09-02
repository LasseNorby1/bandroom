using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Comments;

public sealed record CreateCommentRequest(
    CommentTarget TargetType, Guid TargetId, string Body, double? AtSeconds = null);

public sealed record CommentResponse(
    Guid Id,
    CommentTarget TargetType,
    Guid TargetId,
    Guid AuthorMembershipId,
    string AuthorName,
    string Body,
    double? AtSeconds,
    Instant CreatedAt);

public static class CommentEndpoints
{
    public static IEndpointRouteBuilder MapCommentEndpoints(this IEndpointRouteBuilder routes)
    {
        var comments = routes.MapGroup("/bands/{bandId:guid}/comments")
            .RequireAuthorization()
            .WithTags("comments")
            .AddEndpointFilter<BandMemberFilter>();

        comments.MapGet("/", ListAsync).WithSummary("Comments on an event or demo version");
        comments.MapPost("/", CreateAsync).WithSummary("Comment (optionally at a timestamp)");
        comments.MapDelete("/{commentId:guid}", DeleteAsync).WithSummary("Delete own comment (or as admin)");

        return routes;
    }

    private static async Task<Results<Ok<List<CommentResponse>>, ValidationProblem>> ListAsync(
        string targetType,
        Guid targetId,
        AppDbContext db,
        CancellationToken ct)
    {
        // Query-string enum binding is case-sensitive; the json convention is camelCase.
        if (!Enum.TryParse<CommentTarget>(targetType, ignoreCase: true, out var target))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetType"] = ["Target type must be 'event' or 'demoVersion'."],
            });
        }

        var memberNames = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Id, u.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var comments = (await db.Comments
                .Where(c => c.TargetType == target && c.TargetId == targetId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct))
            .Select(c => new CommentResponse(
                c.Id, c.TargetType, c.TargetId, c.AuthorMembershipId,
                memberNames.GetValueOrDefault(c.AuthorMembershipId, "?"), c.Body, c.AtSeconds, c.CreatedAt))
            .ToList();
        return TypedResults.Ok(comments);
    }

    private static async Task<Results<Created<CommentResponse>, ValidationProblem, NotFound>> CreateAsync(
        CreateCommentRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var body = request.Body?.Trim() ?? "";
        if (body.Length is 0 or > 1000)
        {
            errors["body"] = ["Comment must be 1–1000 characters."];
        }

        if (request.AtSeconds is { } at && (at < 0 || request.TargetType != CommentTarget.DemoVersion))
        {
            errors["atSeconds"] = ["Timestamps only make sense on demo versions."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The target must exist in THIS band (the filters scope these lookups).
        Guid? demoIdeaId = null;
        if (request.TargetType == CommentTarget.Event)
        {
            if (!await db.Events.AnyAsync(e => e.Id == request.TargetId, ct))
            {
                return TypedResults.NotFound();
            }
        }
        else
        {
            demoIdeaId = await db.DemoVersions
                .Where(v => v.Id == request.TargetId)
                .Select(v => (Guid?)v.SongIdeaId)
                .SingleOrDefaultAsync(ct);
            if (demoIdeaId is null)
            {
                return TypedResults.NotFound();
            }
        }

        var comment = new Comment
        {
            BandId = bandContext.BandId!.Value,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            AuthorMembershipId = bandContext.MembershipId!.Value,
            Body = body,
            AtSeconds = request.AtSeconds,
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);

        if (demoIdeaId is { } ideaId)
        {
            await notifier.DemoChangedAsync(comment.BandId, ideaId, "comment", ct);
        }
        else
        {
            await notifier.EventChangedAsync(comment.BandId, request.TargetId, "comment", ct);
        }

        var authorName = await db.Memberships
            .Where(m => m.Id == comment.AuthorMembershipId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.DisplayName)
            .SingleAsync(ct);
        return TypedResults.Created(
            $"/bands/{comment.BandId}/comments/{comment.Id}",
            new CommentResponse(
                comment.Id, comment.TargetType, comment.TargetId, comment.AuthorMembershipId,
                authorName, comment.Body, comment.AtSeconds, comment.CreatedAt));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteAsync(
        Guid commentId,
        BandContext bandContext,
        AppDbContext db,
        CancellationToken ct)
    {
        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId, ct);
        if (comment is null)
        {
            return TypedResults.NotFound();
        }

        if (bandContext.Role != BandRole.Admin && comment.AuthorMembershipId != bandContext.MembershipId)
        {
            return TypedResults.Forbid();
        }

        db.Comments.Remove(comment);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
