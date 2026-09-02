using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Songs;

public sealed record CreateSongRequest(string Title, string? Key = null, int? Bpm = null, string? Notes = null);

public sealed record UpdateSongRequest(string? Title, string? Key, int? Bpm, SongStatus? Status, string? Notes);

public sealed record SongResponse(
    Guid Id, string Title, string? Key, int? Bpm, SongStatus Status, string? Notes, Instant CreatedAt);

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

        return routes;
    }

    private static Dictionary<string, string[]> Validate(string? title, string? key, int? bpm, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
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

    private static async Task<Ok<List<SongResponse>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var songs = await db.Songs
            .OrderBy(s => s.Status)
            .ThenBy(s => s.Title)
            .Select(s => new SongResponse(s.Id, s.Title, s.Key, s.Bpm, s.Status, s.Notes, s.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(songs);
    }

    private static async Task<Results<Created<SongResponse>, ValidationProblem>> CreateAsync(
        CreateSongRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var title = request.Title?.Trim() ?? "";
        var errors = Validate(title, request.Key, request.Bpm, request.Notes);
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
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.Songs.Add(song);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/bands/{song.BandId}/songs/{song.Id}",
            new SongResponse(song.Id, song.Title, song.Key, song.Bpm, song.Status, song.Notes, song.CreatedAt));
    }

    private static async Task<Results<Ok<SongResponse>, NotFound, ValidationProblem>> UpdateAsync(
        Guid songId,
        UpdateSongRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        var errors = Validate(request.Title, request.Key, request.Bpm, request.Notes);
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

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(
            new SongResponse(song.Id, song.Title, song.Key, song.Bpm, song.Status, song.Notes, song.CreatedAt));
    }
}
