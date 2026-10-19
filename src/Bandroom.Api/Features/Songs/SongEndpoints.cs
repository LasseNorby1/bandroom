using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Demos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Songs;

public sealed record CreateSongRequest(
    string Title, string? Key = null, int? Bpm = null, string? Notes = null, int? DurationSeconds = null, string? Chords = null);

/// <summary>Null leaves a field alone; an empty string clears it (key, notes, chords); 0 clears the duration.</summary>
public sealed record UpdateSongRequest(
    string? Title, string? Key, int? Bpm, SongStatus? Status, string? Notes, int? DurationSeconds = null, string? Chords = null);

/// <summary>One row of the songs page: the song and how much hangs off it.</summary>
public sealed record SongResponse(
    Guid Id, string Title, string? Key, int? Bpm, SongStatus Status, string? Notes, Instant CreatedAt,
    int? DurationSeconds, string? Chords, int FireCount, bool FiredByMe, int FileCount,
    int VersionCount, Instant? LatestVersionAt);

/// <summary>The song page: the song plus its takes, stems and polish runs.</summary>
public sealed record SongDetailResponse(
    SongResponse Song,
    IReadOnlyList<VersionResponse> Versions,
    IReadOnlyList<StemResponse> Stems,
    IReadOnlyList<PolishJobResponse> PolishJobs);

public sealed record SongFireResponse(int FireCount, bool FiredByMe);

/// <summary>The song is the one container (spec §5.2): from first riff to repertoire, one id.</summary>
public static class SongEndpoints
{
    public static IEndpointRouteBuilder MapSongEndpoints(this IEndpointRouteBuilder routes)
    {
        var songs = routes.MapGroup("/bands/{bandId:guid}/songs")
            .RequireAuthorization()
            .WithTags("songs")
            .AddEndpointFilter<BandMemberFilter>();

        songs.MapGet("/", ListAsync).WithSummary("Every song, with take and file counts");
        songs.MapPost("/", CreateAsync).WithSummary("Start a song (an idea is a song with status idea)");
        songs.MapGet("/{songId:guid}", DetailAsync).WithSummary("A song with its takes, stems and polish runs");
        songs.MapPatch("/{songId:guid}", UpdateAsync).WithSummary("Update a song: title, key, bpm, status, notes, duration, chords");
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

    private static IQueryable<SongResponse> Project(AppDbContext db, IQueryable<Song> songs, Guid me) =>
        songs.Select(s => new SongResponse(
            s.Id, s.Title, s.Key, s.Bpm, s.Status, s.Notes, s.CreatedAt, s.DurationSeconds, s.Chords,
            db.SongReactions.Count(r => r.SongId == s.Id),
            db.SongReactions.Any(r => r.SongId == s.Id && r.MembershipId == me),
            db.SongFiles.Count(f => f.SongId == s.Id && f.Status == VersionStatus.Ready),
            db.DemoVersions.Count(v => v.SongId == s.Id && v.Status == VersionStatus.Ready),
            db.DemoVersions.Where(v => v.SongId == s.Id && v.Status == VersionStatus.Ready).Max(v => (Instant?)v.CreatedAt)));

    private static async Task<Ok<List<SongResponse>>> ListAsync(AppDbContext db, BandContext bandContext, CancellationToken ct)
    {
        // Newest first; the client groups by status. "Sort by 🔥" is a view there too.
        var songs = await Project(db, db.Songs.OrderByDescending(s => s.CreatedAt), bandContext.MembershipId!.Value).ToListAsync(ct);
        return TypedResults.Ok(songs);
    }

    private static async Task<Results<Ok<SongDetailResponse>, NotFound>> DetailAsync(
        Guid songId, AppDbContext db, BandContext bandContext, CancellationToken ct)
    {
        var me = bandContext.MembershipId!.Value;
        var song = await Project(db, db.Songs.Where(s => s.Id == songId), me).SingleOrDefaultAsync(ct);
        if (song is null)
        {
            return TypedResults.NotFound();
        }

        var takes = await DemoEndpoints.LoadTakesAsync(db, songId, me, ct);
        return TypedResults.Ok(new SongDetailResponse(song, takes.Versions, takes.Stems, takes.PolishJobs));
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
            CreatedByMembershipId = bandContext.MembershipId,
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.Songs.Add(song);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/bands/{song.BandId}/songs/{song.Id}",
            await Project(db, db.Songs.Where(s => s.Id == song.Id), bandContext.MembershipId!.Value).SingleAsync(ct));
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
        return TypedResults.Ok(await Project(db, db.Songs.Where(s => s.Id == songId), bandContext.MembershipId!.Value).SingleAsync(ct));
    }

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
}
