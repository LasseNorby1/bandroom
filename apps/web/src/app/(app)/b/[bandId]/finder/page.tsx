"use client";

import Link from "next/link";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { use } from "react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { bandKeys, useFinder } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import { formatDay, SLOT_LABEL } from "@/lib/format";
import type { FinderCandidate } from "@/lib/types";

function CandidateCard({ bandId, candidate }: { bandId: string; candidate: FinderCandidate }) {
  const router = useRouter();
  const queryClient = useQueryClient();

  const propose = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/events", {
        params: { path: { bandId } },
        body: { type: "practice", date: candidate.date, confirmed: false },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: (detail) => {
      void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) });
      void queryClient.invalidateQueries({ queryKey: bandKeys.finder(bandId) });
      router.push(`/b/${bandId}/events/${detail.event.id}`);
    },
  });

  return (
    <li
      className={cn(
        "flex items-center justify-between gap-4 rounded-xl border bg-surface px-5 py-4",
        candidate.everyoneAvailable ? "border-ok/40" : "border-line",
      )}
    >
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <p className="font-medium">{formatDay(candidate.date)}</p>
          <span
            className={cn(
              "rounded-full px-2 py-0.5 font-mono text-[11px]",
              candidate.everyoneAvailable ? "bg-ok text-white" : "border border-line text-muted",
            )}
          >
            {candidate.everyoneAvailable
              ? "everyone"
              : `${candidate.availableCount}/${candidate.totalMembers}`}
          </span>
        </div>
        <p className="truncate text-sm text-muted">
          {candidate.availableMembers.map((member) => member.displayName).join(", ")}
        </p>
      </div>
      <Button onClick={() => propose.mutate()} disabled={propose.isPending}>
        {propose.isPending ? "Proposing…" : "Propose"}
      </Button>
    </li>
  );
}

export default function FinderPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const finder = useFinder(bandId);

  if (finder.isPending) {
    return <p className="text-sm text-muted">Computing candidate days…</p>;
  }

  if (finder.isError) {
    return <p className="text-sm text-accent">Couldn&apos;t run the finder.</p>;
  }

  const { candidates, days, quorum, slot } = finder.data;

  return (
    <div className="space-y-5">
      <div>
        <p className="font-mono text-xs tracking-wider text-accent">practice finder</p>
        <h2 className="font-display text-xl font-semibold tracking-tight">
          Next {days} days · {SLOT_LABEL[slot]}
        </h2>
        <p className="text-sm text-muted">
          Ranked from everyone&apos;s patterns and blockouts. Practice happens at {quorum}+ going —
          proposing never books anyone.
        </p>
      </div>

      {candidates.length === 0 ? (
        <div className="space-y-3 rounded-xl border border-line bg-surface p-5">
          <p className="text-sm">
            No day reaches quorum. Usually that means patterns aren&apos;t set yet.
          </p>
          <Link href={`/b/${bandId}/availability`} className="inline-block">
            <Button variant="outline">Check availability</Button>
          </Link>
        </div>
      ) : (
        <ul className="space-y-3">
          {candidates.map((candidate) => (
            <CandidateCard key={candidate.date} bandId={bandId} candidate={candidate} />
          ))}
        </ul>
      )}
    </div>
  );
}
