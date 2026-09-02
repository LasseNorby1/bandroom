"use client";

import Link from "next/link";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { bandKeys, useSongIdeas } from "@/lib/band-hooks";
import { formatDay } from "@/lib/format";

const statusLabel: Record<string, string> = {
  idea: "idea",
  inProgress: "in progress",
  finished: "finished",
  parked: "parked",
};

export default function DemosPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const queryClient = useQueryClient();
  const ideas = useSongIdeas(bandId);
  const [title, setTitle] = useState("");

  const create = useMutation({
    mutationFn: async (name: string) => {
      const { data, error } = await api.POST("/bands/{bandId}/song-ideas", {
        params: { path: { bandId } },
        body: { title: name },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setTitle("");
      void queryClient.invalidateQueries({ queryKey: bandKeys.demos(bandId) });
    },
  });

  if (ideas.isPending) {
    return <p className="text-sm text-muted">Loading demos…</p>;
  }

  if (ideas.isError) {
    return <p className="text-sm text-accent">Couldn&apos;t load demos.</p>;
  }

  return (
    <div className="space-y-6">
      <form
        className="flex gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (title.trim()) create.mutate(title.trim());
        }}
      >
        <Input
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="New song idea — e.g. Bridge idea in F#m"
          maxLength={120}
        />
        <Button type="submit" disabled={create.isPending || !title.trim()}>
          Start idea
        </Button>
      </form>

      {ideas.data.length === 0 ? (
        <div className="space-y-2 rounded-xl border border-line bg-surface p-5">
          <h2 className="font-display text-xl font-semibold tracking-tight">
            The riffs live here now
          </h2>
          <p className="text-sm text-muted">
            One idea per song; versions stack underneath — no more fourteen files named
            new_song_final_2.m4a. Record in your voice memo app, upload here.
          </p>
        </div>
      ) : (
        <ul className="space-y-3">
          {ideas.data.map((idea) => (
            <li key={idea.id}>
              <Link
                href={`/b/${bandId}/demos/${idea.id}`}
                className="flex items-center justify-between gap-3 rounded-xl border border-line bg-surface px-5 py-4 transition-colors hover:border-ink"
              >
                <div className="min-w-0">
                  <p className="truncate font-medium">{idea.title}</p>
                  <p className="text-xs text-muted">
                    {idea.versionCount} version{idea.versionCount === 1 ? "" : "s"}
                    {idea.latestVersionAt && ` · latest ${formatDay(idea.latestVersionAt.slice(0, 10))}`}
                  </p>
                </div>
                <span className="rounded-full border border-line px-2.5 py-0.5 font-mono text-[11px] text-muted">
                  {statusLabel[idea.status] ?? idea.status}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
