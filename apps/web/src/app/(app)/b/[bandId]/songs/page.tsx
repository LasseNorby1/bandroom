"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useState } from "react";
import { UploadButton } from "@/components/upload-button";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { bandKeys, useSongs } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import { formatDuration } from "@/lib/duration";
import { formatDay } from "@/lib/format";
import type { Song, SongStatus } from "@/lib/types";
import { uploadDemoVersion } from "@/lib/upload";

/**
 * One list for everything the band plays or might: a song from first riff to
 * repertoire (spec §5.2, revised oct 2026). Grouped by where it is in its life,
 * nobody files anything; "ready" is the repertoire the setlist draws from.
 */
const GROUPS: { key: SongStatus; label: string; folded: boolean }[] = [
  { key: "working", label: "working on", folded: false },
  { key: "idea", label: "ideas", folded: false },
  { key: "ready", label: "ready · the repertoire", folded: false },
  { key: "parked", label: "parked", folded: true },
];



export default function SongsPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const router = useRouter();
  const queryClient = useQueryClient();
  const songs = useSongs(bandId);
  const [title, setTitle] = useState("");
  const [openGroups, setOpenGroups] = useState<string[]>([]);
  const [byFire, setByFire] = useState(false);

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });

  const create = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/songs", {
        params: { path: { bandId } },
        body: { title: title.trim(), key: null, bpm: null, notes: null },
      });
      if (error || !data) throw error ?? new Error("no song");
      return data;
    },
    onSuccess: (song) => {
      setTitle("");
      invalidate();
      router.push(`/b/${bandId}/songs/${song.id}`);
    },
  });

  // "Upload here" means here: a take starts a song named after the file.
  async function uploadAsNewSong(file: File, onProgress: (fraction: number) => void) {
    const name = file.name.replace(/\.[^.]+$/, "").replace(/[_-]+/g, " ").trim() || "New song";
    const { data: song, error } = await api.POST("/bands/{bandId}/songs", {
      params: { path: { bandId } },
      body: { title: name.slice(0, 120), key: null, bpm: null, notes: null },
    });
    if (error || !song) throw error ?? new Error("couldn't start the song");
    await uploadDemoVersion(bandId, song.id, file, onProgress);
    invalidate();
    router.push(`/b/${bandId}/songs/${song.id}`);
  }

  const fire = useMutation({
    mutationFn: async (song: Song) => {
      const path = "/bands/{bandId}/songs/{songId}/fire" as const;
      const params = { params: { path: { bandId, songId: song.id } } };
      const { error } = song.firedByMe ? await api.DELETE(path, params) : await api.PUT(path, params);
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  if (songs.isPending) {
    return <p className="text-sm text-muted">Loading songs…</p>;
  }

  if (songs.isError) {
    return <p className="text-sm text-accent">Couldn&apos;t load songs.</p>;
  }

  const activity = (s: Song) => s.latestVersionAt ?? s.createdAt;
  const order = (a: Song, b: Song) =>
    byFire ? b.fireCount - a.fireCount || activity(b).localeCompare(activity(a)) : activity(b).localeCompare(activity(a));

  return (
    <div className="space-y-6">
      <form
        className="flex gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (title.trim()) create.mutate();
        }}
      >
        <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="New song — a title is enough, e.g. Bridge idea in F#m" maxLength={120} />
        <Button type="submit" disabled={create.isPending || !title.trim()}>Start song</Button>
      </form>
      <div className="flex flex-wrap items-center gap-3">
        <UploadButton label="Upload a take" busyLabel="Uploading" accept="audio/*" variant="outline" onFile={uploadAsNewSong} />
        <span className="text-xs text-faint">A take uploaded here starts a song named after the file. Recording, more takes, chords and files live on the song.</span>
      </div>

      {songs.data.length === 0 ? (
        <div className="space-y-2 rounded-xl border border-line bg-surface p-5">
          <h2 className="font-display text-xl font-semibold tracking-tight">The songs live here now</h2>
          <p className="text-sm text-muted">
            One song from first riff to repertoire: takes stack underneath, chords and files sit beside them —
            no more fourteen files named new_song_final_2.m4a.
          </p>
        </div>
      ) : (
        <div className="space-y-6">
          <div className="flex items-center justify-between">
            <p className="font-mono text-xs tracking-wider text-muted">{songs.data.length} songs</p>
            <button type="button" onClick={() => setByFire(!byFire)} aria-pressed={byFire} className="font-mono text-[11px] text-muted hover:text-ink">
              {byFire ? "sorted by 🔥 · by activity instead" : "sort by 🔥"}
            </button>
          </div>
          {GROUPS.map((group) => {
            const members = songs.data.filter((s) => s.status === group.key).sort(order);
            if (members.length === 0) return null;
            const folded = group.folded && !openGroups.includes(group.key);
            return (
              <section key={group.key} className="space-y-2">
                <button
                  type="button"
                  onClick={() => group.folded && setOpenGroups(folded ? [...openGroups, group.key] : openGroups.filter((k) => k !== group.key))}
                  aria-expanded={group.folded ? !folded : undefined}
                  className={cn("flex items-center gap-2 font-mono text-xs tracking-wider text-muted", group.folded && "hover:text-ink")}
                >
                  {group.label} · {members.length}
                  {group.folded && <span className="text-faint">{folded ? "show" : "hide"}</span>}
                </button>
                {!folded && (
                  <ul className="divide-y divide-line rounded-xl border border-line bg-surface">
                    {members.map((song) => (
                      <li key={song.id} className="flex items-center justify-between gap-3 px-5 py-3">
                        <Link href={`/b/${bandId}/songs/${song.id}`} className="min-w-0 flex-1 hover:text-ink">
                          <p className="truncate font-medium">{song.title}</p>
                          <p className="font-mono text-[11px] text-muted">
                            {[
                              song.key,
                              song.bpm != null && `${song.bpm} bpm`,
                              song.durationSeconds != null && formatDuration(song.durationSeconds),
                              `${song.versionCount} take${song.versionCount === 1 ? "" : "s"}`,
                              song.fileCount > 0 && `${song.fileCount} file${song.fileCount === 1 ? "" : "s"}`,
                              song.chords && "chords",
                              song.latestVersionAt && `latest ${formatDay(song.latestVersionAt.slice(0, 10))}`,
                            ].filter(Boolean).join(" · ")}
                          </p>
                        </Link>
                        <button
                          type="button"
                          onClick={() => fire.mutate(song)}
                          disabled={fire.isPending}
                          aria-pressed={song.firedByMe}
                          aria-label={song.firedByMe ? "Take the fire back" : "Fire"}
                          className={cn(
                            "shrink-0 rounded-full border px-2 py-0.5 font-mono text-[11px] transition-colors",
                            song.firedByMe ? "border-accent/50 bg-accent-soft text-accent" : "border-line text-muted hover:border-ink hover:text-ink",
                          )}
                        >
                          🔥{song.fireCount > 0 && ` ${song.fireCount}`}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}
