"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { bandKeys, useSongs } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";

export default function SongsPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const queryClient = useQueryClient();
  const songs = useSongs(bandId);
  const [title, setTitle] = useState("");
  const [key, setKey] = useState("");
  const [bpm, setBpm] = useState("");

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

  if (songs.isPending) {
    return <p className="text-sm text-muted">Loading the repertoire…</p>;
  }

  if (songs.isError) {
    return <p className="text-sm text-accent">Couldn&apos;t load songs.</p>;
  }

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
        <ul className="divide-y divide-line rounded-xl border border-line bg-surface">
          {songs.data.map((song) => (
            <li key={song.id} className="flex items-center justify-between gap-3 px-5 py-3">
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
                  {song.notes && ` · ${song.notes}`}
                </p>
              </div>
              <button
                type="button"
                onClick={() => toggleStatus.mutate(song)}
                className="shrink-0 font-mono text-[11px] text-muted hover:text-ink"
              >
                {song.status === "active" ? "retire" : "revive"}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
