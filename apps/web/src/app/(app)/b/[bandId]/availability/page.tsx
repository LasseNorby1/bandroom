"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useBandAvailability, useMyAvailability } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import { addDays, DAY_ORDER, DAY_SHORT, formatDay, isoDayOfDate, mondayOf, SLOT_LABEL, todayIso, weeksBetween } from "@/lib/format";
import type { BlockoutException, IsoDay, MemberAvailability, PracticeSlot, WeeklySlot } from "@/lib/types";
import { useBandContext as useBand } from "@/lib/band-context";

const SLOTS: PracticeSlot[] = ["evening", "afternoon"];

const slotKey = (day: IsoDay, slot: PracticeSlot) => `${day}:${slot}`;

type WeekSet = "a" | "b";

function slotsOf(keys: Set<string>): WeeklySlot[] {
  return [...keys].map((key) => {
    const [day, slot] = key.split(":") as [IsoDay, PracticeSlot];
    return { day, slot };
  });
}

function keysOf(slots: WeeklySlot[] | undefined): Set<string> {
  return new Set((slots ?? []).map((slot) => slotKey(slot.day as IsoDay, slot.slot)));
}

function sameKeys(a: Set<string>, b: Set<string>): boolean {
  return a.size === b.size && [...a].every((key) => b.has(key));
}

/** Which of the two sets the anchor puts in force this week. */
function thisWeekFromAnchor(anchorMonday: string | null | undefined): WeekSet {
  if (!anchorMonday) return "a";
  return ((weeksBetween(anchorMonday, mondayOf(todayIso())) % 2) + 2) % 2 === 0 ? "a" : "b";
}

function SlotGrid({ open, onToggle, label }: { open: Set<string>; onToggle: (day: IsoDay, slot: PracticeSlot) => void; label?: string }) {
  return (
    <div className="overflow-x-auto">
      <table className="border-separate border-spacing-1">
        <thead>
          <tr>
            <th aria-hidden className="w-20" />
            {DAY_ORDER.map((day) => (
              <th key={day} className="pb-1 font-mono text-[11px] font-normal text-muted">
                {DAY_SHORT[day]}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {SLOTS.map((slot) => (
            <tr key={slot}>
              <td className="pr-2 font-mono text-[11px] text-muted">{slot}</td>
              {DAY_ORDER.map((day) => {
                const active = open.has(slotKey(day, slot));
                return (
                  <td key={day}>
                    <button
                      type="button"
                      aria-pressed={active}
                      aria-label={`${label ? `${label} ` : ""}${day} ${slot}`}
                      onClick={() => onToggle(day, slot)}
                      className={cn(
                        "size-10 rounded-lg border transition-colors",
                        active ? "border-ok/50 bg-ok-soft" : "border-dashed border-line hover:border-faint",
                      )}
                    />
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PatternEditor({ bandId }: { bandId: string }) {
  const queryClient = useQueryClient();
  const mine = useMyAvailability(bandId);
  // The grids show the server's pattern until the first tap; from then on the
  // local edits, until they're saved (which clears them and shows the server's
  // again). Derived, not synced — no effect, no cascade.
  type Draft = { a: Set<string>; b: Set<string>; alternating: boolean; thisWeek: WeekSet };
  const [draft, setDraft] = useState<Draft | null>(null);
  const server = useMemo<Draft>(
    () => ({
      a: keysOf(mine.data?.openSlots),
      b: keysOf(mine.data?.alternateOpenSlots),
      alternating: !!mine.data?.anchorMonday,
      thisWeek: thisWeekFromAnchor(mine.data?.anchorMonday),
    }),
    [mine.data],
  );
  const current = draft ?? server;
  const dirty =
    draft !== null &&
    (draft.alternating !== server.alternating ||
      !sameKeys(draft.a, server.a) ||
      (draft.alternating && (!sameKeys(draft.b, server.b) || draft.thisWeek !== server.thisWeek)));

  const save = useMutation({
    mutationFn: async () => {
      // The anchor is a Monday whose week uses set A: this Monday if "this week
      // is A", otherwise next Monday. Never iso week parity — it flips in 53-week years.
      const thisMonday = mondayOf(todayIso());
      const body = current.alternating
        ? { openSlots: slotsOf(current.a), alternateOpenSlots: slotsOf(current.b), anchorMonday: current.thisWeek === "a" ? thisMonday : addDays(thisMonday, 7) }
        : { openSlots: slotsOf(current.a) };
      const { data, error } = await api.PUT("/bands/{bandId}/availability/me/pattern", {
        params: { path: { bandId } },
        body,
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setDraft(null);
      void queryClient.invalidateQueries({ queryKey: bandKeys.availability(bandId) });
    },
  });

  function update(change: Partial<Draft>) {
    setDraft({ ...current, a: new Set(current.a), b: new Set(current.b), ...change });
  }

  function toggle(set: WeekSet, day: IsoDay, slot: PracticeSlot) {
    const key = slotKey(day, slot);
    const next = new Set(current[set]);
    if (next.has(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }
    update({ [set]: next });
  }

  const modeButton = (alternating: boolean, text: string) => (
    <button
      type="button"
      aria-pressed={current.alternating === alternating}
      onClick={() => update({ alternating })}
      className={cn(
        "rounded-full border px-3 py-1 text-xs transition-colors",
        current.alternating === alternating ? "border-ink bg-ink text-paper" : "border-line text-muted hover:border-faint",
      )}
    >
      {text}
    </button>
  );

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-medium">Your weekly pattern</h2>
          <p className="text-sm text-muted">
            Tap the slots that generally work for you — set once, then only report exceptions.
          </p>
        </div>
        <div className="flex gap-1.5" role="group" aria-label="Rhythm">
          {modeButton(false, "Same every week")}
          {modeButton(true, "Every other week")}
        </div>
      </div>

      {current.alternating ? (
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Two weeks that alternate — say, the week with the kids and the week without. Then tell us which one this is.
          </p>
          <div className="space-y-5">
            {(["a", "b"] as WeekSet[]).map((set) => (
              <div key={set} className="space-y-2">
                <div className="flex items-center justify-between">
                  <span className="font-mono text-[11px] uppercase tracking-wide text-muted">Week {set.toUpperCase()}</span>
                  <label className="flex items-center gap-1.5 text-xs text-muted">
                    <input
                      type="radio"
                      name="this-week"
                      checked={current.thisWeek === set}
                      onChange={() => update({ thisWeek: set })}
                    />
                    this is the current week
                  </label>
                </div>
                <SlotGrid open={current[set]} onToggle={(day, slot) => toggle(set, day, slot)} label={`week ${set}`} />
              </div>
            ))}
          </div>
        </div>
      ) : (
        <SlotGrid open={current.a} onToggle={(day, slot) => toggle("a", day, slot)} />
      )}

      <div className="flex items-center gap-3">
        <Button onClick={() => save.mutate()} disabled={!dirty || save.isPending}>
          {save.isPending ? "Saving…" : "Save pattern"}
        </Button>
        {!dirty && mine.data?.patternUpdatedAt && (
          <p className="text-xs text-faint">Saved.</p>
        )}
        {save.isError && <p className="text-xs text-accent">Couldn&apos;t save — try again.</p>}
      </div>
    </section>
  );
}

function Blockouts({ bandId }: { bandId: string }) {
  const queryClient = useQueryClient();
  const mine = useMyAvailability(bandId);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [note, setNote] = useState("");

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: bandKeys.availability(bandId) });
  };

  const add = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/availability/me/exceptions", {
        params: { path: { bandId } },
        body: { from, to: to || from, note: note || null },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setFrom("");
      setTo("");
      setNote("");
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: async (exceptionId: string) => {
      const { error } = await api.DELETE("/bands/{bandId}/availability/me/exceptions/{exceptionId}", {
        params: { path: { bandId, exceptionId } },
      });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  // "Can't the next two weekends": the roadie proposes, you confirm (spec §5.5).
  const { detail } = useBand();
  const aiOn = !!detail?.band.aiAvailable && !!detail?.band.aiEnabled;
  const [phrase, setPhrase] = useState("");
  const [proposed, setProposed] = useState<{ from: string; to: string; note: string | null; keep: boolean }[] | null>(null);
  const [unparsed, setUnparsed] = useState<string | null>(null);
  const parse = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/availability/me/exceptions/parse", {
        params: { path: { bandId } },
        body: { text: phrase },
      });
      if (error || !data) throw error ?? new Error("no answer");
      return data;
    },
    onSuccess: (data) => {
      setProposed(data.blockouts.map((b) => ({ ...b, keep: true })));
      setUnparsed(data.unparsed ?? null);
    },
  });
  const addProposed = useMutation({
    mutationFn: async () => {
      for (const b of proposed ?? []) {
        if (!b.keep) continue;
        const { error } = await api.POST("/bands/{bandId}/availability/me/exceptions", {
          params: { path: { bandId } },
          body: { from: b.from, to: b.to, note: b.note },
        });
        if (error) throw error;
      }
    },
    onSuccess: () => {
      setProposed(null);
      setPhrase("");
      invalidate();
    },
  });

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Your blockouts</h2>
        <p className="text-sm text-muted">Dates you can&apos;t make it — &quot;can&apos;t the 12th–15th&quot;.</p>
      </div>

      {mine.data && mine.data.exceptions.length > 0 && (
        <ul className="space-y-2">
          {mine.data.exceptions.map((exception: BlockoutException) => (
            <li
              key={exception.id}
              className="flex items-center justify-between rounded-lg border border-line px-3.5 py-2.5 text-sm"
            >
              <span>
                {formatDay(exception.from)}
                {exception.to !== exception.from && <> – {formatDay(exception.to)}</>}
                {exception.note && <span className="text-muted"> · {exception.note}</span>}
              </span>
              <button
                type="button"
                onClick={() => remove.mutate(exception.id)}
                className="text-xs text-muted hover:text-accent"
              >
                remove
              </button>
            </li>
          ))}
        </ul>
      )}

      {aiOn && (
        <div className="space-y-2 rounded-lg border border-dashed border-line p-3">
          <form
            className="flex gap-2"
            onSubmit={(event) => {
              event.preventDefault();
              if (phrase.trim()) parse.mutate();
            }}
          >
            <Input
              value={phrase}
              onChange={(e) => setPhrase(e.target.value)}
              placeholder="Say it: “can't the next two weekends, tuesdays out until June”"
              maxLength={500}
              className="h-9"
            />
            <Button type="submit" size="sm" variant="outline" disabled={parse.isPending || !phrase.trim()}>
              {parse.isPending ? "Reading…" : "Propose"}
            </Button>
          </form>
          {parse.isError && <p className="text-xs text-accent">Couldn&apos;t read that. Try plainer words, or use the fields below.</p>}
          {proposed && (
            <div className="space-y-2">
              {proposed.length === 0 && <p className="text-xs text-muted">No dates found in that.</p>}
              <ul className="space-y-1">
                {proposed.map((b, i) => (
                  <li key={`${b.from}-${i}`} className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={b.keep}
                      onChange={(e) => setProposed(proposed.map((x, j) => (j === i ? { ...x, keep: e.target.checked } : x)))}
                    />
                    <span>
                      {formatDay(b.from)}
                      {b.to !== b.from && <> – {formatDay(b.to)}</>}
                      {b.note && <span className="text-muted"> · {b.note}</span>}
                    </span>
                  </li>
                ))}
              </ul>
              {unparsed && <p className="text-xs text-faint">Couldn&apos;t place: “{unparsed}”</p>}
              {proposed.some((b) => b.keep) && (
                <Button size="sm" onClick={() => addProposed.mutate()} disabled={addProposed.isPending}>
                  {addProposed.isPending ? "Adding…" : `Add ${proposed.filter((b) => b.keep).length} blockout${proposed.filter((b) => b.keep).length === 1 ? "" : "s"}`}
                </Button>
              )}
            </div>
          )}
        </div>
      )}

      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (from) add.mutate();
        }}
      >
        <div className="space-y-1">
          <label htmlFor="from" className="block text-xs text-muted">from</label>
          <Input id="from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} required className="w-40" />
        </div>
        <div className="space-y-1">
          <label htmlFor="to" className="block text-xs text-muted">to (optional)</label>
          <Input id="to" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} className="w-40" />
        </div>
        <div className="min-w-40 flex-1 space-y-1">
          <label htmlFor="note" className="block text-xs text-muted">note (optional)</label>
          <Input id="note" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} placeholder="travel" />
        </div>
        <Button type="submit" variant="outline" disabled={!from || add.isPending}>
          Add
        </Button>
      </form>
      {add.isError && <p className="text-xs text-accent">Couldn&apos;t add that blockout.</p>}
    </section>
  );
}

function CalendarOverlay({ bandId }: { bandId: string }) {
  const queryClient = useQueryClient();
  const mine = useMyAvailability(bandId);
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: async (value: string) => {
      const { error } = await api.PUT("/bands/{bandId}/availability/me/calendar", {
        params: { path: { bandId } },
        body: { url: value },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      setUrl(null);
      setError(null);
      void queryClient.invalidateQueries({ queryKey: bandKeys.availability(bandId) });
    },
    onError: (err: unknown) => {
      const problem = err as { errors?: Record<string, string[]> } | undefined;
      setError(problem?.errors ? Object.values(problem.errors).flat().join(" ") : "Couldn't save that.");
    },
  });

  if (!mine.data) return null;
  const status = mine.data.calendarImport;
  const editing = url !== null;

  return (
    <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Your calendar, overlaid</h2>
        <p className="text-sm text-muted">
          Paste the private ics address of your own calendar and days it fills show as busy here, in the finder too.
          Read-only, refreshed every six hours, nothing is written back.
        </p>
      </div>
      {status.url && !editing ? (
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <code className="min-w-0 max-w-full truncate rounded bg-paper px-2 py-1 font-mono text-xs">{status.url}</code>
          <span className="font-mono text-[11px] text-muted">
            {status.error ? <span className="text-accent">{status.error}</span> : `${status.busyDays} busy slot${status.busyDays === 1 ? "" : "s"} in the next 60 days`}
          </span>
          <button type="button" onClick={() => setUrl(status.url ?? "")} className="text-xs text-muted hover:text-ink">change</button>
          <button type="button" onClick={() => save.mutate("")} className="text-xs text-muted hover:text-accent">remove</button>
        </div>
      ) : (
        <form
          className="flex flex-wrap gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            save.mutate(url ?? "");
          }}
        >
          <Input
            value={url ?? ""}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://calendar.google.com/calendar/ical/…/private-…/basic.ics"
            className="min-w-64 flex-1"
          />
          <Button type="submit" variant="outline" disabled={save.isPending || !(url ?? "").trim()}>
            {save.isPending ? "Reading…" : "Overlay it"}
          </Button>
          {editing && status.url && (
            <Button type="button" variant="ghost" onClick={() => setUrl(null)}>Cancel</Button>
          )}
        </form>
      )}
      {error && <p className="text-xs text-accent">{error}</p>}
      <p className="text-xs text-faint">
        Google: calendar settings → &ldquo;Secret address in iCal format&rdquo;. Apple: share the calendar as public, copy the link. Outlook: publish calendar, ICS link.
      </p>
    </section>
  );
}

type CellState = "free" | "blocked" | "busy" | "off";

function memberStateOn(member: MemberAvailability, date: string, slot: PracticeSlot): CellState {
  if (member.exceptions.some((e) => e.from <= date && date <= e.to)) {
    return "blocked";
  }
  const day = isoDayOfDate(date);
  const alternateWeek =
    !!member.anchorMonday && ((weeksBetween(member.anchorMonday, mondayOf(date)) % 2) + 2) % 2 === 1;
  const open = alternateWeek ? member.alternateOpenSlots : member.openSlots;
  if (!open.some((s) => s.day === day && s.slot === slot)) {
    return "off";
  }
  if (member.importedBusy.some((b) => b.date === date && (b.slot === null || b.slot === slot))) {
    return "busy";
  }
  return "free";
}

function WeekGrid({ bandId }: { bandId: string }) {
  const { detail } = useBandContext();
  const availability = useBandAvailability(bandId, 14);
  const slot = detail?.band.defaultPracticeSlot ?? "evening";

  const dates = useMemo(() => {
    if (!availability.data) return [];
    return Array.from({ length: 14 }, (_, offset) => addDays(availability.data.from, offset));
  }, [availability.data]);

  if (availability.isPending) {
    return <p className="text-sm text-muted">Loading the band grid…</p>;
  }

  if (availability.isError || !availability.data) {
    return null;
  }

  const members = availability.data.members;
  const total = members.length;

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">The band — next two weeks</h2>
        <p className="text-sm text-muted">{SLOT_LABEL[slot]} · patterns and blockouts overlaid</p>
      </div>
      <div className="overflow-x-auto">
        <table className="border-separate border-spacing-1">
          <thead>
            <tr>
              <th aria-hidden className="min-w-20" />
              {dates.map((date) => (
                <th key={date} className="pb-1 font-mono text-[10px] font-normal text-muted">
                  <div>{DAY_SHORT[isoDayOfDate(date)]}</div>
                  <div className="text-faint">{date.slice(8)}</div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {members.map((member) => (
              <tr key={member.membershipId}>
                <td className="max-w-24 truncate pr-2 text-sm">{member.displayName}</td>
                {dates.map((date) => {
                  const state = memberStateOn(member, date, slot);
                  return (
                    <td key={date}>
                      <div
                        title={`${member.displayName} · ${formatDay(date)} · ${state}`}
                        className={cn(
                          "size-8 rounded-md",
                          state === "free" && "bg-ok-soft",
                          state === "blocked" && "border border-line bg-paper",
                          state === "busy" && "border border-accent/30 bg-accent-soft/60",
                          state === "off" && "border border-dashed border-line",
                        )}
                      >
                        {state === "blocked" && (
                          <span className="flex h-full items-center justify-center font-mono text-[10px] text-faint">✕</span>
                        )}
                        {state === "busy" && (
                          <span className="flex h-full items-center justify-center font-mono text-[10px] text-accent">◔</span>
                        )}
                      </div>
                    </td>
                  );
                })}
              </tr>
            ))}
            <tr>
              <td className="pr-2 font-mono text-[11px] text-muted">everyone?</td>
              {dates.map((date) => {
                const free = members.filter((m) => memberStateOn(m, date, slot) === "free").length;
                const everyone = total > 0 && free === total;
                return (
                  <td key={date} className="text-center">
                    <span
                      className={cn(
                        "inline-block rounded-full px-1.5 py-0.5 font-mono text-[10px]",
                        everyone ? "bg-ok text-white" : "text-muted",
                      )}
                    >
                      {free}/{total}
                    </span>
                  </td>
                );
              })}
            </tr>
          </tbody>
        </table>
      </div>
      <div className="flex gap-4 font-mono text-[11px] text-muted">
        <span className="flex items-center gap-1.5"><span className="size-3 rounded bg-ok-soft" /> free</span>
        <span className="flex items-center gap-1.5"><span className="flex size-3 items-center justify-center rounded border border-line text-[8px]">✕</span> blockout</span>
        <span className="flex items-center gap-1.5"><span className="flex size-3 items-center justify-center rounded border border-accent/30 bg-accent-soft/60 text-[8px] text-accent">◔</span> busy (calendar)</span>
        <span className="flex items-center gap-1.5"><span className="size-3 rounded border border-dashed border-line" /> outside pattern</span>
      </div>
    </section>
  );
}

export default function AvailabilityPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);

  return (
    <div className="space-y-6">
      <PatternEditor bandId={bandId} />
      <Blockouts bandId={bandId} />
      <CalendarOverlay bandId={bandId} />
      <WeekGrid bandId={bandId} />
    </div>
  );
}
