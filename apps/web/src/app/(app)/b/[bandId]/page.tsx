"use client";

import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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

/** "What did I miss": what the others did since you last said you were caught up. */
function CatchUp({ bandId }: { bandId: string }) {
  const queryClient = useQueryClient();
  const key = ["band", bandId, "catch-up"] as const;
  const catchUp = useQuery({
    queryKey: key,
    staleTime: 60_000,
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/catch-up", { params: { path: { bandId } } });
      if (error) throw error;
      return data;
    },
  });
  const seen = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/bands/{bandId}/catch-up/seen", { params: { path: { bandId } } });
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: key }),
  });

  if (!catchUp.data || catchUp.data.items.length === 0) return null;
  const { items, summary } = catchUp.data;

  return (
    <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
      <div className="flex items-baseline justify-between gap-4">
        <p className="font-mono text-xs tracking-wider text-accent">since you were here</p>
        <button type="button" onClick={() => seen.mutate()} disabled={seen.isPending} className="text-xs text-muted hover:text-ink">
          got it
        </button>
      </div>
      {summary && <p className="text-sm">{summary}</p>}
      <ul className="space-y-1">
        {items.slice(0, summary ? 6 : 12).map((item, i) => (
          <li key={i} className="flex gap-2 text-sm text-muted">
            <span className="font-mono text-[10px] text-faint">{item.kind}</span>
            {item.path ? (
              <Link href={item.path} className="min-w-0 truncate hover:text-ink hover:underline underline-offset-4">{item.text}</Link>
            ) : (
              <span className="min-w-0 truncate">{item.text}</span>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

/** A gig, a deadline, anything that isn't a practice — typed in, or drafted from a pasted venue email. */
function AddEvent({ bandId }: { bandId: string }) {
  const { detail } = useBandContext();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [pasted, setPasted] = useState("");
  const [draft, setDraft] = useState({ type: "gig", date: "", startTime: "", endTime: "", title: "", location: "" });
  const [note, setNote] = useState<string | null>(null);
  const aiOn = !!detail?.band.aiAvailable && !!detail?.band.aiEnabled;

  const parse = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/events/parse-offer", { params: { path: { bandId } }, body: { text: pasted } });
      if (error || !data) throw error ?? new Error("no answer");
      return data;
    },
    onSuccess: (offer) => {
      setDraft({
        type: offer.type ?? "gig",
        date: offer.date ?? "",
        startTime: offer.startTime?.slice(0, 5) ?? "",
        endTime: offer.endTime?.slice(0, 5) ?? "",
        title: offer.title ?? "",
        location: offer.location ?? "",
      });
      setNote([offer.fee && `fee: ${offer.fee}`, offer.notes].filter(Boolean).join(" · ") || null);
    },
  });

  const create = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/events", {
        params: { path: { bandId } },
        body: {
          type: draft.type as "gig" | "deadline" | "other" | "practice",
          date: draft.date,
          slot: null,
          startTime: draft.startTime ? `${draft.startTime}:00` : undefined,
          endTime: draft.endTime ? `${draft.endTime}:00` : undefined,
          title: draft.title || null,
          location: draft.location || null,
          confirmed: false,
        },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setOpen(false);
      setPasted("");
      setDraft({ type: "gig", date: "", startTime: "", endTime: "", title: "", location: "" });
      setNote(null);
      void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) });
    },
  });

  if (!open) {
    return (
      <button type="button" onClick={() => setOpen(true)} className="font-mono text-xs tracking-wider text-muted hover:text-ink">
        + add a gig or deadline
      </button>
    );
  }

  const field = "h-10 w-full rounded-lg border border-line bg-surface px-3 text-sm";
  return (
    <form
      className="space-y-3 rounded-xl border border-line bg-surface p-5"
      onSubmit={(event) => {
        event.preventDefault();
        if (draft.date) create.mutate();
      }}
    >
      <p className="font-mono text-xs tracking-wider text-accent">new event</p>
      {aiOn && (
        <div className="flex gap-2">
          <textarea
            value={pasted}
            onChange={(e) => setPasted(e.target.value)}
            rows={2}
            maxLength={4000}
            placeholder="Paste the venue's email and the roadie fills in the form"
            className="min-h-10 flex-1 rounded-lg border border-line bg-paper px-3 py-2 text-sm placeholder:text-faint"
          />
          <Button type="button" variant="outline" size="sm" disabled={parse.isPending || !pasted.trim()} onClick={() => parse.mutate()}>
            {parse.isPending ? "Reading…" : "Draft it"}
          </Button>
        </div>
      )}
      {note && <p className="text-xs text-muted">{note}</p>}
      <div className="grid gap-3 sm:grid-cols-3">
        <select value={draft.type} onChange={(e) => setDraft({ ...draft, type: e.target.value })} className={field} aria-label="Type">
          <option value="gig">gig</option>
          <option value="deadline">deadline</option>
          <option value="other">other</option>
        </select>
        <input type="date" required value={draft.date} onChange={(e) => setDraft({ ...draft, date: e.target.value })} className={field} aria-label="Date" />
        <input value={draft.title} onChange={(e) => setDraft({ ...draft, title: e.target.value })} placeholder="title" maxLength={120} className={field} aria-label="Title" />
        <input type="time" value={draft.startTime} onChange={(e) => setDraft({ ...draft, startTime: e.target.value })} className={field} aria-label="Start" />
        <input type="time" value={draft.endTime} onChange={(e) => setDraft({ ...draft, endTime: e.target.value })} className={field} aria-label="End" />
        <input value={draft.location} onChange={(e) => setDraft({ ...draft, location: e.target.value })} placeholder="location" maxLength={200} className={field} aria-label="Location" />
      </div>
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={create.isPending || !draft.date}>Propose it</Button>
        <Button type="button" size="sm" variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
        {create.isError && <p className="text-xs text-accent">Couldn&apos;t save — check the date.</p>}
      </div>
    </form>
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
      <CatchUp bandId={bandId} />
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

      <AddEvent bandId={bandId} />

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
