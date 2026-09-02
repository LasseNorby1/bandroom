"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use } from "react";
import { RsvpButtons } from "@/components/rsvp-buttons";
import { StatusChip } from "@/components/status-chip";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useEvent } from "@/lib/band-hooks";
import { formatDayLong, SLOT_LABEL } from "@/lib/format";
import type { RsvpStatus } from "@/lib/types";

const rsvpLabel: Record<RsvpStatus, string> = {
  going: "going",
  notGoing: "can't",
  maybe: "maybe",
};

export default function EventPage({
  params,
}: {
  params: Promise<{ bandId: string; eventId: string }>;
}) {
  const { bandId, eventId } = use(params);
  const { me, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const event = useEvent(bandId, eventId);

  const transition = useMutation({
    mutationFn: async (action: "confirm" | "cancel") => {
      const path =
        action === "confirm"
          ? ("/bands/{bandId}/events/{eventId}/confirm" as const)
          : ("/bands/{bandId}/events/{eventId}/cancel" as const);
      const { data, error } = await api.POST(path, { params: { path: { bandId, eventId } } });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) });
    },
  });

  if (event.isPending) {
    return <p className="text-sm text-muted">Loading…</p>;
  }

  if (event.isError || !event.data) {
    return <p className="text-sm text-accent">That event doesn&apos;t exist (anymore).</p>;
  }

  const { event: evt, rsvps } = event.data;
  const myRsvp = rsvps.find((rsvp) => rsvp.membershipId === me?.membershipId);
  const title = evt.title ?? (evt.type === "practice" ? "Practice" : evt.type);
  const time = evt.startTime
    ? `${evt.startTime.slice(0, 5)}${evt.endTime ? `–${evt.endTime.slice(0, 5)}` : ""}`
    : evt.slot
      ? SLOT_LABEL[evt.slot]
      : "";

  return (
    <div className="space-y-6">
      <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="font-mono text-xs tracking-wider text-muted">{evt.type}</p>
            <h2 className="font-display text-xl font-semibold tracking-tight">{title}</h2>
            <p className="text-sm text-muted">
              {formatDayLong(evt.date)}
              {time && ` · ${time}`}
              {evt.location && ` · ${evt.location}`}
            </p>
          </div>
          <StatusChip status={evt.status} />
        </div>

        {evt.status !== "cancelled" && (
          <RsvpButtons bandId={bandId} eventId={eventId} myStatus={myRsvp?.status} />
        )}

        {isAdmin && evt.status !== "cancelled" && (
          <div className="flex gap-2 border-t border-line pt-4">
            {evt.status === "proposed" && (
              <Button
                variant="outline"
                size="sm"
                disabled={transition.isPending}
                onClick={() => transition.mutate("confirm")}
              >
                Confirm now
              </Button>
            )}
            <Button
              variant="ghost"
              size="sm"
              disabled={transition.isPending}
              onClick={() => transition.mutate("cancel")}
            >
              Cancel event
            </Button>
          </div>
        )}
      </section>

      <section className="space-y-2">
        <p className="font-mono text-xs tracking-wider text-muted">
          rsvps · {evt.goingCount} going
        </p>
        <ul className="divide-y divide-line rounded-xl border border-line bg-surface">
          {rsvps.map((rsvp) => (
            <li key={rsvp.membershipId} className="flex items-center justify-between px-5 py-3">
              <div>
                <p className="text-sm font-medium">{rsvp.displayName}</p>
                {rsvp.note && <p className="text-xs text-muted">{rsvp.note}</p>}
              </div>
              <span className="font-mono text-[11px] text-muted">{rsvpLabel[rsvp.status]}</span>
            </li>
          ))}
          {rsvps.length === 0 && (
            <li className="px-5 py-3 text-sm text-muted">No answers yet.</li>
          )}
        </ul>
      </section>
    </div>
  );
}
