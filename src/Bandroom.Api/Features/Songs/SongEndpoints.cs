using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Songs;

public sealed record CreateSongRequest(
    string Title, string? Key = null, int? Bpm = null, string? Notes = null, int? DurationSeconds = null, string? Chords = null);

/// <summary>Null leaves a field alone; an empty string clears it (key, notes, chords); 0 clears the duration.</summary>
public sealed record UpdateSongRequest(
    string? Title, string? Key, int? Bpm, SongStatus? Status, string? Notes, int? DurationSeconds = null, string? Chords = null);

public sealed record SongResponse(
    Guid Id, string Title, string? Key, int? Bpm, SongStatus Status, string? Notes, Instant CreatedAt,
    int? DurationSeconds, string? Chords, int FireCount, bool FiredByMe);

public sealed record SongFireResponse(int FireCount, bool FiredByMe);

public static class SongEndpoints
{
    public static IEndpointRouteBuilder MapSongEndpoints(this IEndpointRouteBuilder routes)
    {
        var songs = routes.MapGroup("/bands/{bandId:guid}/songs")
            .RequireAuthorization()
            .WithTags("songs")
            .AddEndpointFilter<BandMemberFilter>();

        songs.MapGet("/", ListAsync).WithSummary("The repertoire");
        songs.MapPost("/", CreateAsync).WithSummary("Add a song");
        songs.MapPatch("/{songId:guid}", UpdateAsync).WithSummary("Update a song");
        songs.MapPut("/{songId:guid}/fire", FireAsync).WithSummary("🔥 this song");
        songs.MapDelete("/{songId:guid}/fire", UnfireAsync).WithSummary("Take the 🔥 back");

        return routes;
    }

    private static Dictionary<string, string[]> Validate(
        string? title, string? key, int? bpm, string? notes, int? durationSeconds = null, string? chords = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (durationSeconds is < 0 or > 3 * 60 * 60)
        {
            errors["durationSeconds"] = ["Duration must be between 0 and 3 hours."];
        }

        if (chords is { Length: > 20000 })
        {
            errors["chords"] = ["Chords must be at most 20 000 characters."];
        }

        if (title is not null && (title.Trim().Length is 0 or > 120))
        {
            errors["title"] = ["Title must be 1–120 characters."];
        }

        if (key is { Length: > 12 })
        {
            errors["key"] = ["Key must be at most 12 characters."];
        }

        if (bpm is < 20 or > 400)
        {
            errors["bpm"] = ["Bpm must be between 20 and 400."];
        }

        if (notes is { Length: > 2000 })
        {
            errors["notes"] = ["Notes must be at most 2000 characters."];
        }

        return errors;
    }

    private static async Task<Ok<List<SongResponse>>> ListAsync(AppDbContext db, BandContext bandContext, CancellationToken ct)
    {
        var me = bandContext.MembershipId!.Value;
        var songs = await db.Songs
            .OrderBy(s => s.Status)
            .ThenBy(s => s.Title)
            .Select(s => new SongResponse(
                s.Id, s.Title, s.Key, s.Bpm, s.Status, s.Notes, s.CreatedAt, s.DurationSeconds, s.Chords,
                db.SongReactions.Count(r => r.SongId == s.Id),
                db.SongReactions.Any(r => r.SongId == s.Id && r.MembershipId == me)))
            .ToListAsync(ct);
        return TypedResults.Ok(songs);
    }

    private static async Task<SongResponse> ToResponseAsync(AppDbContext db, Song song, Guid me, CancellationToken ct) =>
        new(song.Id, song.Title, song.Key, song.Bpm, song.Status, song.Notes, song.CreatedAt, song.DurationSeconds, song.Chords,
            await db.SongReactions.CountAsync(r => r.SongId == song.Id, ct),
            await db.SongReactions.AnyAsync(r => r.SongId == song.Id && r.MembershipId == me, ct));

    private static async Task<Results<Ok<SongFireResponse>, NotFound>> FireAsync(
        Guid songId, BandContext bandContext, AppDbContext db, IClock clock, CancellationToken ct)
    {
        if (!await db.Songs.AnyAsync(s => s.Id == songId, ct))
        {
            return TypedResults.NotFound();
        }

        var me = bandContext.MembershipId!.Value;
        if (!await db.SongReactions.AnyAsync(r => r.SongId == songId && r.MembershipId == me, ct))
        {
            db.SongReactions.Add(new SongReaction
            {
                BandId = bandContext.BandId!.Value, SongId = songId, MembershipId = me, CreatedAt = clock.GetCurrentInstant(),
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Double tap raced the unique index — already lit.
            }
        }

        return TypedResults.Ok(new SongFireResponse(await db.SongReactions.CountAsync(r => r.SongId == songId, ct), true));
    }

    private static async Task<Results<Ok<SongFireResponse>, NotFound>> UnfireAsync(
        Guid songId, BandContext bandContext, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Songs.AnyAsync(s => s.Id == songId, ct))
        {
            return TypedResults.NotFound();
        }

        var me = bandContext.MembershipId!.Value;
        await db.SongReactions.Where(r => r.SongId == songId && r.MembershipId == me).ExecuteDeleteAsync(ct);
        return TypedResults.Ok(new SongFireResponse(await db.SongReactions.CountAsync(r => r.SongId == songId, ct), false));
    }

    private static async Task<Results<Created<SongResponse>, ValidationProblem>> CreateAsync(
        CreateSongRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var title = request.Title?.Trim() ?? "";
        var errors = Validate(title, request.Key, request.Bpm, request.Notes, request.DurationSeconds, request.Chords);
        if (title.Length == 0)
        {
            errors["title"] = ["Title is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var song = new Song
        {
            BandId = bandContext.BandId!.Value,
            Title = title,
            Key = request.Key?.Trim(),
            Bpm = request.Bpm,
            Notes = request.Notes?.Trim(),
            DurationSeconds = request.DurationSeconds is > 0 ? request.DurationSeconds : null,
            Chords = string.IsNullOrWhiteSpace(request.Chords) ? null : request.Chords.Trim(),
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.Songs.Add(song);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/bands/{song.BandId}/songs/{song.Id}",
            await ToResponseAsync(db, song, bandContext.MembershipId!.Value, ct));
    }

    private static async Task<Results<Ok<SongResponse>, NotFound, ValidationProblem>> UpdateAsync(
        Guid songId,
        UpdateSongRequest request,
        BandContext bandContext,
        AppDbContext db,
        CancellationToken ct)
    {
        var errors = Validate(request.Title, request.Key, request.Bpm, request.Notes, request.DurationSeconds, request.Chords);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var song = await db.Songs.SingleOrDefaultAsync(s => s.Id == songId, ct);
        if (song is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Title is not null)
        {
            song.Title = request.Title.Trim();
        }

        if (request.Key is not null)
        {
            song.Key = request.Key.Trim() is { Length: 0 } ? null : request.Key.Trim();
        }

        if (request.Bpm is not null)
        {
            song.Bpm = request.Bpm;
        }

        if (request.Status is { } status)
        {
            song.Status = status;
        }

        if (request.Notes is not null)
        {
            song.Notes = request.Notes.Trim() is { Length: 0 } ? null : request.Notes.Trim();
        }

        if (request.DurationSeconds is { } duration)
        {
            song.DurationSeconds = duration > 0 ? duration : null;
        }

        if (request.Chords is not null)
        {
            song.Chords = string.IsNullOrWhiteSpace(request.Chords) ? null : request.Chords.Trim();
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToResponseAsync(db, song, bandContext.MembershipId!.Value, ct));
    }
}
