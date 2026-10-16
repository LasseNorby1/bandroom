"use client";

import Link from "next/link";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useState } from "react";
import { api } from "@/lib/api";
import { bandKeys } from "@/lib/band-hooks";
import { RsvpButtons } from "@/components/rsvp-buttons";
import { StatusChip } from "@/components/status-chip";
import { Button } from "@/components/ui/button";
import { useBandContext } from "@/lib/band-context";
import { useEvents, useMyAvailability } from "@/lib/band-hooks";
import { formatDay, formatDayLong, SLOT_LABEL } from "@/lib/format";
import type { EventSummary } from "@/lib/types";

function eventTitle(evt: EventSummary): string {
  if (evt.title) return evt.title;
  return evt.type === "practice" ? "Practice" : evt.type;
}

function eventTimeLabel(evt: EventSummary): string {
  if (evt.startTime) {
    return `${evt.startTime.slice(0, 5)}${evt.endTime ? `–${evt.endTime.slice(0, 5)}` : ""}`;
  }
  return evt.slot ? SLOT_LABEL[evt.slot] : "";
}

function Noticeboard({ bandId }: { bandId: string }) {
  const { detail, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState("");

  const save = useMutation({
    mutationFn: async (value: string) => {
      const { error } = await api.PUT("/bands/{bandId}/notice", { params: { path: { bandId } }, body: { text: value } });
      if (error) throw error;
    },
    onSuccess: () => {
      setEditing(false);
      void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
    },
  });

  if (!detail) return null;
  const notice = detail.band.notice;
  if (!notice && !isAdmin) return null;

  if (editing) {
    return (
      <form
        className="space-y-2 rounded-xl border border-line bg-surface p-5"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(text);
        }}
      >
        <p className="font-mono text-xs tracking-wider text-accent">noticeboard</p>
        <textarea
          value={text}
          onChange={(event) => setText(event.target.value)}
          maxLength={2000}
          rows={3}
          autoFocus
          placeholder="Door code, load-in time, the one thing everyone should know."
          className="w-full rounded-lg border border-line bg-paper px-3.5 py-2 text-sm text-ink placeholder:text-faint focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent"
        />
        <div className="flex gap-2">
          <Button type="submit" size="sm" disabled={save.isPending}>Pin it</Button>
          <Button type="button" size="sm" variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
          {notice && (
            <Button type="button" size="sm" variant="ghost" className="ml-auto text-accent" onClick={() => save.mutate("")}>
              Take down
            </Button>
          )}
        </div>
      </form>
    );
  }

  return (
    <section className="flex items-start justify-between gap-4 rounded-xl border border-line bg-surface p-5">
      <div className="min-w-0 space-y-1">
        <p className="font-mono text-xs tracking-wider text-accent">noticeboard</p>
        {notice ? (
          <p className="whitespace-pre-wrap text-sm">{notice}</p>
        ) : (
          <p className="text-sm text-muted">Nothing pinned. The one note the whole band should see goes here.</p>
        )}
      </div>
      {isAdmin && (
        <button
          type="button"
          onClick={() => {
            setText(notice ?? "");
            setEditing(true);
          }}
          className="shrink-0 text-xs text-muted hover:text-ink"
        >
          {notice ? "edit" : "pin a note"}
        </button>
      )}
    </section>
  );
}

function NextUp({ bandId, evt }: { bandId: string; evt: EventSummary }) {
  const { detail } = useBandContext();

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="font-mono text-xs tracking-wider text-accent">next up</p>
          <Link href={`/b/${bandId}/events/${evt.id}`} className="block hover:underline underline-offset-4">
            <h2 className="font-display text-xl font-semibold tracking-tight">
              {eventTitle(evt)} — {formatDayLong(evt.date)}
            </h2>
          </Link>
          <p className="text-sm text-muted">
            {eventTimeLabel(evt)}
            {evt.location ? ` · ${evt.location}` : ""}
            {` · ${evt.goingCount} going`}
          </p>
        </div>
        <StatusChip status={evt.status} />
      </div>
      {evt.status !== "cancelled" && (
        <RsvpButtons bandId={bandId} eventId={evt.id} myStatus={evt.myRsvp ?? undefined} />
      )}
      {evt.status === "proposed" && detail && (
        <p className="text-xs text-faint">
          Confirms automatically at {detail.band.quorum} going.
        </p>
      )}
    </section>
  );
}

export default function BandHomePage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const events = useEvents(bandId);
  const mine = useMyAvailability(bandId);

  if (events.isPending) {
    return <p className="text-sm text-muted">Loading…</p>;
  }

  if (events.isError) {
    return <p className="text-sm text-accent">Couldn&apos;t load events.</p>;
  }

  const upcoming = events.data.filter((evt) => evt.status !== "cancelled");
  const [next, ...rest] = upcoming;

  return (
    <div className="space-y-6">
      <Noticeboard bandId={bandId} />
      {mine.data && mine.data.openSlots.length === 0 && (
        <div className="flex items-center justify-between gap-4 rounded-xl border border-accent/30 bg-accent-soft px-5 py-4">
          <p className="text-sm">
            <span className="font-medium">Set your weekly pattern</span>
            <span className="text-muted"> — thirty seconds, once. The finder needs it.</span>
          </p>
          <Link href={`/b/${bandId}/availability`}>
            <Button size="sm">Set it</Button>
          </Link>
        </div>
      )}

      {next ? (
        <NextUp bandId={bandId} evt={next} />
      ) : (
        <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
          <h2 className="font-display text-xl font-semibold tracking-tight">Nothing scheduled</h2>
          <p className="text-sm text-muted">
            The finder computes days where enough of you are free — no group-chat poll.
          </p>
          <Link href={`/b/${bandId}/finder`} className="inline-block">
            <Button>Find a practice day</Button>
          </Link>
        </section>
      )}

      {rest.length > 0 && (
        <section className="space-y-2">
          <p className="font-mono text-xs tracking-wider text-muted">coming up</p>
          <ul className="divide-y divide-line rounded-xl border border-line bg-surface">
            {rest.map((evt) => (
              <li key={evt.id}>
                <Link
                  href={`/b/${bandId}/events/${evt.id}`}
                  className="flex items-center justify-between gap-3 px-5 py-3 hover:bg-paper"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">
                      {formatDay(evt.date)} — {eventTitle(evt)}
                    </p>
                    <p className="text-xs text-muted">
                      {eventTimeLabel(evt)}
                      {` · ${evt.goingCount} going`}
                    </p>
                  </div>
                  <StatusChip status={evt.status} />
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
