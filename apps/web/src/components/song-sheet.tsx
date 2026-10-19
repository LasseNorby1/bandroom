"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { ChordSheet } from "@/components/chord-sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { bandKeys } from "@/lib/band-hooks";
import { formatDuration, parseDuration } from "@/lib/duration";
import type { Song } from "@/lib/types";

/** Duration and inline chords on the song (spec §2, borrowed from BandHelper). */
export function SongSheet({ bandId, song }: { bandId: string; song: Song }) {
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
