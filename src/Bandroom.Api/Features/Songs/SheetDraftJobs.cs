using System.Text;
using System.Text.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Demos;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure.Storage;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Songs;

public interface ISheetDraftQueue
{
    void Enqueue(Guid draftId);
}

public sealed class HangfireSheetDraftQueue : ISheetDraftQueue
{
    public void Enqueue(Guid draftId) =>
        BackgroundJob.Enqueue<SheetDraftRunner>(runner => runner.RunAsync(draftId, CancellationToken.None));
}

public sealed class NoopSheetDraftQueue(ILogger<NoopSheetDraftQueue> logger) : ISheetDraftQueue
{
    public void Enqueue(Guid draftId) => logger.LogWarning("jobs disabled — sheet draft {DraftId} stays queued", draftId);
}

/// <summary>The worker's answer: words with times, chords with times.</summary>
public sealed record SheetWord(double Start, double End, string Text);

public sealed record SheetSegment(double Start, double End, IReadOnlyList<SheetWord> Words);

public sealed record SheetChord(double Start, double End, string Chord);

public sealed record SheetResponse(
    string? Language, double? LanguageProbability, IReadOnlyList<SheetSegment> Segments, IReadOnlyList<SheetChord> Chords, string? Model);

/// <summary>Two timelines → one ChordPro draft: a chord goes in front of the word it starts under.</summary>
public static class ChordProDraft
{
    public static string Build(IReadOnlyList<SheetSegment> segments, IReadOnlyList<SheetChord> chords)
    {
        var sb = new StringBuilder();
        string? lastChord = null;
        var chordIndex = 0;
        double? previousEnd = null;

        static bool StartsALine(string word) =>
            word.Length > 0 && char.IsUpper(word[0]) && !word.All(char.IsUpper);

        static string? ChordAt(IReadOnlyList<SheetChord> chords, ref int index, double at)
        {
            while (index < chords.Count && chords[index].End <= at)
            {
                index++;
            }

            return index < chords.Count && chords[index].Start <= at ? chords[index].Chord : null;
        }

        foreach (var segment in segments)
        {
            // A pause of a few seconds between lines usually means a new verse.
            if (previousEnd is { } end && segment.Start - end > 4)
            {
                sb.AppendLine();
            }

            var first = true;
            foreach (var word in segment.Words)
            {
                var chord = ChordAt(chords, ref chordIndex, word.Start + 0.05);
                if (!first)
                {
                    // Whisper runs two sung lines into one segment and marks the seam with a
                    // capital ("…still asleep I can hear…"); a sheet wants them on two lines.
                    sb.Append(StartsALine(word.Text) ? '\n' : ' ');
                }

                if (chord is not null && chord != lastChord)
                {
                    sb.Append('[').Append(chord).Append(']');
                    lastChord = chord;
                }

                sb.Append(word.Text);
                first = false;
            }

            sb.AppendLine();
            previousEnd = segment.End;
        }

        // Chords that never met a word (an intro, an outro) go on their own lines.
        if (segments.Count == 0 && chords.Count > 0)
        {
            sb.AppendLine(string.Join(" ", chords.Select(c => $"[{c.Chord}]")));
        }

        return sb.ToString().TrimEnd() + "\n";
    }
}

/// <summary>Runs off the request path, writes only the SheetDraft row, notifies the band so the song page refreshes.</summary>
public sealed class SheetDraftRunner(
    AppDbContext db,
    IFileStorage storage,
    IHttpClientFactory httpClientFactory,
    WorkerOptions worker,
    BandNotifier notifier,
    IClock clock,
    ILogger<SheetDraftRunner> logger)
{
    private static readonly JsonSerializerOptions WorkerJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record SheetRequest(string Url, string? Language);

    public async Task RunAsync(Guid draftId, CancellationToken ct)
    {
        var draft = await db.Set<SheetDraft>().IgnoreQueryFilters().SingleOrDefaultAsync(d => d.Id == draftId, ct);
        if (draft is null || draft.Status != AnalysisStatus.Queued)
        {
            return;
        }

        var version = await db.DemoVersions.IgnoreQueryFilters().SingleOrDefaultAsync(v => v.Id == draft.VersionId, ct);
        if (version is null)
        {
            draft.Status = AnalysisStatus.Failed;
            draft.Error = "That take is gone.";
            await db.SaveChangesAsync(ct);
            return;
        }

        if (string.IsNullOrEmpty(worker.Url))
        {
            draft.Status = AnalysisStatus.Failed;
            draft.Error = "No audio worker configured.";
            draft.CompletedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            var url = storage.GetDownloadUrl(version.FileKey, TimeSpan.FromHours(1), StorageAudience.Worker);
            // A band that says what it sings in spares whisper the guess — the single biggest miss on mixed demos.
            var language = await db.Bands.IgnoreQueryFilters().Where(b => b.Id == draft.BandId).Select(b => b.LyricsLanguage).SingleOrDefaultAsync(ct);
            var http = httpClientFactory.CreateClient("worker");
            var response = await http.PostAsJsonAsync($"{worker.Url.TrimEnd('/')}/sheet", new SheetRequest(url, language), WorkerJson, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SheetResponse>(WorkerJson, ct)
                         ?? throw new InvalidOperationException("Empty worker response.");

            draft.Draft = ChordProDraft.Build(result.Segments, result.Chords);
            draft.Language = result.Language;
            draft.Model = result.Model;
            draft.Status = AnalysisStatus.Done;
            draft.CompletedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(ct);
            await notifier.DemoChangedAsync(draft.BandId, draft.SongId, "sheetDraft", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "sheet draft {DraftId} failed", draftId);
            draft.Status = AnalysisStatus.Failed;
            draft.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            draft.CompletedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(CancellationToken.None);
            await notifier.DemoChangedAsync(draft.BandId, draft.SongId, "sheetDraft", CancellationToken.None);
        }
    }
}
