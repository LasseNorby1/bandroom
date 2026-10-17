"use client";

import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { use, useRef, useState } from "react";
import { ChordSheet } from "@/components/chord-sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useSongs } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import { formatDuration, parseDuration } from "@/lib/duration";
import type { Song } from "@/lib/types";
import { formatBytes, uploadSongFile } from "@/lib/upload";

/** Lyrics, chords, charts — the paper that belongs to a song. */
function SongFiles({ bandId, songId }: { bandId: string; songId: string }) {
  const { me, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const key = ["band", bandId, "songs", songId, "files"] as const;

  const files = useQuery({
    queryKey: key,
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/songs/{songId}/files", { params: { path: { bandId, songId } } });
      if (error) throw error;
      return data;
    },
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: key });
    void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
    void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });
  };

  const remove = useMutation({
    mutationFn: async (fileId: string) => {
      const { error } = await api.DELETE("/bands/{bandId}/song-files/{fileId}", { params: { path: { bandId, fileId } } });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  async function open(fileId: string) {
    const { data } = await api.GET("/bands/{bandId}/song-files/{fileId}/download", { params: { path: { bandId, fileId } } });
    if (data) window.open(data.url, "_blank", "noopener");
  }

  return (
    <div className="space-y-2 border-t border-line px-5 py-3">
      {files.data && files.data.length > 0 && (
        <ul className="space-y-1">
          {files.data.map((file) => (
            <li key={file.id} className="flex items-center gap-3 text-sm">
              <button type="button" onClick={() => void open(file.id)} className="min-w-0 truncate underline-offset-4 hover:underline">
                {file.title}
              </button>
              <span className="font-mono text-[11px] text-faint">
                {file.fileName.split(".").pop()?.toLowerCase()} · {formatBytes(file.sizeBytes)} · {file.uploaderName}
              </span>
              {(isAdmin || file.uploaderName === me?.displayName) && (
                <button type="button" onClick={() => remove.mutate(file.id)} className="ml-auto text-[11px] text-faint hover:text-accent">
                  remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      <div className="flex items-center gap-3">
        <input
          ref={inputRef}
          type="file"
          accept=".pdf,.txt,.md,.png,.jpg,.jpeg,.webp,application/pdf,text/plain,text/markdown,image/*"
          className="hidden"
          onChange={async (event) => {
            const file = event.target.files?.[0];
            event.target.value = "";
            if (!file) return;
            setError(null);
            setProgress(0);
            try {
              await uploadSongFile(bandId, songId, file, setProgress);
              invalidate();
            } catch {
              setError("Upload failed — pdf, text or image up to 20 MB.");
            } finally {
              setProgress(null);
            }
          }}
        />
        <button
          type="button"
          disabled={progress !== null}
          onClick={() => inputRef.current?.click()}
          className="font-mono text-[11px] text-muted hover:text-ink disabled:opacity-50"
        >
          {progress !== null ? `uploading ${Math.round(progress * 100)}%` : "+ lyrics, chords or a chart"}
        </button>
        {error && <span className="text-xs text-accent">{error}</span>}
      </div>
    </div>
  );
}

/** Duration and inline chords on the song (spec §2, borrowed from BandHelper). */
function SongSheet({ bandId, song }: { bandId: string; song: Song }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(!song.chords);
  const [chords, setChords] = useState(song.chords ?? "");
  const [duration, setDuration] = useState(formatDuration(song.durationSeconds));
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: async () => {
      const seconds = parseDuration(duration);
      if (duration.trim() && seconds === null) throw new Error("Duration as m:ss, e.g. 3:35.");
      const { error } = await api.PATCH("/bands/{bandId}/songs/{songId}", {
        params: { path: { bandId, songId: song.id } },
        body: { title: null, key: null, bpm: null, status: null, notes: null, durationSeconds: seconds ?? 0, chords },
      });
      if (error) throw new Error("Couldn't save.");
    },
    onSuccess: () => {
      setEditing(false);
      setError(null);
      void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });
    },
    onError: (e) => setError(e instanceof Error ? e.message : "Couldn't save."),
  });

  if (!editing) {
    return (
      <div className="space-y-2 border-t border-line px-5 py-3">
        {song.chords ? <ChordSheet text={song.chords} /> : <p className="text-sm text-muted">No lyrics or chords yet.</p>}
        <button type="button" onClick={() => setEditing(true)} className="font-mono text-[11px] text-muted hover:text-ink">edit</button>
      </div>
    );
  }

  return (
    <form
      className="space-y-2 border-t border-line px-5 py-3"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate();
      }}
    >
      <div className="flex flex-wrap items-end gap-2">
        <label className="space-y-1 text-xs text-muted">
          duration
          <Input value={duration} onChange={(e) => setDuration(e.target.value)} placeholder="3:35" className="w-24" />
        </label>
        <p className="text-xs text-faint">The setlist adds these up. Chords in brackets: [Am]Hold the [F]line.</p>
      </div>
      <textarea
        value={chords}
        onChange={(e) => setChords(e.target.value)}
        rows={8}
        maxLength={20000}
        placeholder={"[Am]Hold the [F]line\n[C]Till the morning [G]comes"}
        className="w-full rounded-lg border border-line bg-paper px-3 py-2 font-mono text-[13px] placeholder:text-faint"
      />
      <div className="flex items-center gap-2">
        <Button type="submit" size="sm" disabled={save.isPending}>{save.isPending ? "Saving…" : "Save"}</Button>
        <Button type="button" size="sm" variant="ghost" onClick={() => { setEditing(false); setChords(song.chords ?? ""); setDuration(formatDuration(song.durationSeconds)); }}>Cancel</Button>
        {error && <span className="text-xs text-accent">{error}</span>}
      </div>
    </form>
  );
}

export default function SongsPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const queryClient = useQueryClient();
  const songs = useSongs(bandId);
  const [title, setTitle] = useState("");
  const [key, setKey] = useState("");
  const [bpm, setBpm] = useState("");
  const [openSong, setOpenSong] = useState<string | null>(null);
  const [openSheet, setOpenSheet] = useState<string | null>(null);
  const [byFire, setByFire] = useState(false);

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });

  const create = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/songs", {
        params: { path: { bandId } },
        body: {
          title: title.trim(),
          key: key.trim() || null,
          bpm: bpm ? Number(bpm) : null,
          notes: null,
        },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setTitle("");
      setKey("");
      setBpm("");
      invalidate();
    },
  });

  const toggleStatus = useMutation({
    mutationFn: async (song: { id: string; status: string }) => {
      const { error } = await api.PATCH("/bands/{bandId}/songs/{songId}", {
        params: { path: { bandId, songId: song.id } },
        body: {
          title: null,
          key: null,
          bpm: null,
          status: song.status === "active" ? "retired" : "active",
          notes: null,
        },
      });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

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
    return <p className="text-sm text-muted">Loading the repertoire…</p>;
  }

  if (songs.isError) {
    return <p className="text-sm text-accent">Couldn&apos;t load songs.</p>;
  }

  // The api orders by status then title; "by 🔥" is a view, the same one signal as on takes.
  const ordered = byFire
    ? [...songs.data].sort((a, b) => (a.status === b.status ? b.fireCount - a.fireCount || a.title.localeCompare(b.title) : a.status === "active" ? -1 : 1))
    : songs.data;

  return (
    <div className="space-y-6">
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (title.trim()) create.mutate();
        }}
      >
        <div className="min-w-44 flex-1 space-y-1">
          <label htmlFor="title" className="block text-xs text-muted">title</label>
          <Input id="title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} required />
        </div>
        <div className="space-y-1">
          <label htmlFor="key" className="block text-xs text-muted">key</label>
          <Input id="key" value={key} onChange={(e) => setKey(e.target.value)} placeholder="F#m" maxLength={12} className="w-20" />
        </div>
        <div className="space-y-1">
          <label htmlFor="bpm" className="block text-xs text-muted">bpm</label>
          <Input id="bpm" type="number" min={20} max={400} value={bpm} onChange={(e) => setBpm(e.target.value)} className="w-24" />
        </div>
        <Button type="submit" disabled={create.isPending || !title.trim()}>
          Add song
        </Button>
      </form>

      {songs.data.length === 0 ? (
        <p className="rounded-xl border border-dashed border-line p-5 text-sm text-muted">
          The repertoire is empty — add the songs you play, link demo ideas to them later.
        </p>
      ) : (
        <>
          <div className="flex items-center justify-between">
            <p className="font-mono text-xs tracking-wider text-muted">{songs.data.length} songs</p>
            <button type="button" onClick={() => setByFire(!byFire)} aria-pressed={byFire} className="font-mono text-[11px] text-muted hover:text-ink">
              {byFire ? "sorted by 🔥 · a–z instead" : "sort by 🔥"}
            </button>
          </div>
          <ul className="divide-y divide-line rounded-xl border border-line bg-surface">
            {ordered.map((song) => (
              <li key={song.id}>
                <div className="flex items-center justify-between gap-3 px-5 py-3">
                  <div className="min-w-0">
                    <p
                      className={cn(
                        "truncate text-sm font-medium",
                        song.status === "retired" && "text-faint line-through",
                      )}
                    >
                      {song.title}
                    </p>
                    <p className="font-mono text-[11px] text-muted">
                      {song.key ?? "—"}
                      {song.bpm != null && ` · ${song.bpm} bpm`}
                      {song.durationSeconds != null && ` · ${formatDuration(song.durationSeconds)}`}
                      {song.notes && ` · ${song.notes}`}
                    </p>
                    {song.ideas.length > 0 && (
                      <p className="font-mono text-[11px] text-muted">
                        demos:{" "}
                        {song.ideas.map((idea, i) => (
                          <span key={idea.id}>
                            {i > 0 && ", "}
                            <Link href={`/b/${bandId}/demos/${idea.id}`} className="text-ink underline decoration-line underline-offset-2">
                              {idea.title}
                            </Link>
                            {idea.versionCount > 0 && ` (${idea.versionCount})`}
                          </span>
                        ))}
                      </p>
                    )}
                  </div>
                  <span className="flex shrink-0 items-center gap-3">
                    <button
                      type="button"
                      onClick={() => fire.mutate(song)}
                      disabled={fire.isPending}
                      aria-pressed={song.firedByMe}
                      aria-label={song.firedByMe ? "Take the fire back" : "Fire"}
                      className={cn(
                        "rounded-full border px-2 py-0.5 font-mono text-[11px] transition-colors",
                        song.firedByMe ? "border-accent/50 bg-accent-soft text-accent" : "border-line text-muted hover:border-ink hover:text-ink",
                      )}
                    >
                      🔥{song.fireCount > 0 && ` ${song.fireCount}`}
                    </button>
                    <button
                      type="button"
                      onClick={() => setOpenSheet(openSheet === song.id ? null : song.id)}
                      aria-expanded={openSheet === song.id}
                      className="font-mono text-[11px] text-muted hover:text-ink"
                    >
                      {song.chords ? "chords" : "chords +"}
                    </button>
                    <button
                      type="button"
                      onClick={() => setOpenSong(openSong === song.id ? null : song.id)}
                      aria-expanded={openSong === song.id}
                      className="font-mono text-[11px] text-muted hover:text-ink"
                    >
                      {song.fileCount > 0 ? `files · ${song.fileCount}` : "files +"}
                    </button>
                    <button
                      type="button"
                      onClick={() => toggleStatus.mutate(song)}
                      className="font-mono text-[11px] text-muted hover:text-ink"
                    >
                      {song.status === "active" ? "retire" : "revive"}
                    </button>
                  </span>
                </div>
                {openSheet === song.id && <SongSheet key={`${song.id}-${song.chords ?? ""}-${song.durationSeconds ?? ""}`} bandId={bandId} song={song} />}
                {openSong === song.id && <SongFiles bandId={bandId} songId={song.id} />}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
