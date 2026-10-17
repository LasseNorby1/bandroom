"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useState } from "react";
import { UploadButton } from "@/components/upload-button";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { bandKeys, useSongIdeas } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import { formatDay } from "@/lib/format";
import { uploadDemoVersion } from "@/lib/upload";

/**
 * Order without filing: ideas fall into place by where they are in their life,
 * and the done and the parked fold away. No folders to maintain (principle 1).
 */
const GROUPS: { key: string; label: string; statuses: string[]; folded: boolean }[] = [
  { key: "working", label: "in progress", statuses: ["inProgress"], folded: false },
  { key: "ideas", label: "ideas", statuses: ["idea"], folded: false },
  { key: "finished", label: "finished", statuses: ["finished"], folded: true },
  { key: "parked", label: "parked", statuses: ["parked"], folded: true },
];

const statusLabel: Record<string, string> = {
  idea: "idea",
  inProgress: "in progress",
  finished: "finished",
  parked: "parked",
};

export default function DemosPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const router = useRouter();
  const queryClient = useQueryClient();
  const ideas = useSongIdeas(bandId);
  const [title, setTitle] = useState("");
  const [openGroups, setOpenGroups] = useState<string[]>([]);

  // "Upload here" has to mean here: a take dropped on the overview starts an
  // idea named after the file and lands as its first version.
  async function uploadAsNewIdea(file: File, onProgress: (fraction: number) => void) {
    const name = file.name.replace(/\.[^.]+$/, "").replace(/[_-]+/g, " ").trim() || "New idea";
    const { data: idea, error } = await api.POST("/bands/{bandId}/song-ideas", {
      params: { path: { bandId } },
      body: { title: name.slice(0, 120) },
    });
    if (error || !idea) throw error ?? new Error("couldn't start the idea");
    await uploadDemoVersion(bandId, idea.id, file, onProgress);
    void queryClient.invalidateQueries({ queryKey: bandKeys.demos(bandId) });
    router.push(`/b/${bandId}/demos/${idea.id}`);
  }

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
      <div className="flex flex-wrap items-center gap-3">
        <UploadButton label="Upload a take" busyLabel="Uploading" accept="audio/*" variant="outline" onFile={uploadAsNewIdea} />
        <span className="text-xs text-faint">A take uploaded here starts an idea named after the file. Recording and more takes live inside each idea.</span>
      </div>

      {ideas.data.length === 0 ? (
        <div className="space-y-2 rounded-xl border border-line bg-surface p-5">
          <h2 className="font-display text-xl font-semibold tracking-tight">
            The riffs live here now
          </h2>
          <p className="text-sm text-muted">
            One idea per song; versions stack underneath — no more fourteen files named
            new_song_final_2.m4a. Upload a take above, or start an idea and record straight into it.
          </p>
        </div>
      ) : (
        <div className="space-y-6">
          {GROUPS.map((group) => {
            const members = ideas.data
              .filter((idea) => group.statuses.includes(idea.status))
              .sort((a, b) => (b.latestVersionAt ?? b.createdAt).localeCompare(a.latestVersionAt ?? a.createdAt));
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
                  <ul className="space-y-3">
                    {members.map((idea) => (
                      <li key={idea.id}>
                        <Link
                          href={`/b/${bandId}/demos/${idea.id}`}
                          className="flex items-center justify-between gap-3 rounded-xl border border-line bg-surface px-5 py-4 transition-colors hover:border-ink"
                        >
                          <div className="min-w-0">
                            <p className="truncate font-medium">{idea.title}</p>
                            <p className="text-xs text-muted">
                              {idea.songTitle && <span className="text-ink">{idea.songTitle} · </span>}
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
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}
