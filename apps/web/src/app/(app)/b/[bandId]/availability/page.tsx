"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useBandAvailability, useMyAvailability } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import { addDays, DAY_ORDER, DAY_SHORT, formatDay, isoDayOfDate, SLOT_LABEL } from "@/lib/format";
import type { BlockoutException, IsoDay, MemberAvailability, PracticeSlot, WeeklySlot } from "@/lib/types";

const SLOTS: PracticeSlot[] = ["evening", "afternoon"];

const slotKey = (day: IsoDay, slot: PracticeSlot) => `${day}:${slot}`;

function PatternEditor({ bandId }: { bandId: string }) {
  const queryClient = useQueryClient();
  const mine = useMyAvailability(bandId);
  const [open, setOpen] = useState<Set<string>>(new Set());
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (mine.data && !dirty) {
      setOpen(new Set(mine.data.openSlots.map((slot) => slotKey(slot.day as IsoDay, slot.slot))));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- sync from server until edited
  }, [mine.data]);

  const save = useMutation({
    mutationFn: async () => {
      const openSlots: WeeklySlot[] = [...open].map((key) => {
        const [day, slot] = key.split(":") as [IsoDay, PracticeSlot];
        return { day, slot };
      });
      const { data, error } = await api.PUT("/bands/{bandId}/availability/me/pattern", {
        params: { path: { bandId } },
        body: { openSlots },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setDirty(false);
      void queryClient.invalidateQueries({ queryKey: bandKeys.availability(bandId) });
    },
  });

  function toggle(day: IsoDay, slot: PracticeSlot) {
    const key = slotKey(day, slot);
    const next = new Set(open);
    if (next.has(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }
    setOpen(next);
    setDirty(true);
  }

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Your weekly pattern</h2>
        <p className="text-sm text-muted">
          Tap the slots that generally work for you — set once, then only report exceptions.
        </p>
      </div>
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
                        aria-label={`${day} ${slot}`}
                        onClick={() => toggle(day, slot)}
                        className={cn(
                          "size-10 rounded-lg border transition-colors",
                          active
                            ? "border-ok/50 bg-ok-soft"
                            : "border-dashed border-line hover:border-faint",
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

type CellState = "free" | "blocked" | "off";

function memberStateOn(member: MemberAvailability, date: string, slot: PracticeSlot): CellState {
  if (member.exceptions.some((e) => e.from <= date && date <= e.to)) {
    return "blocked";
  }
  const day = isoDayOfDate(date);
  return member.openSlots.some((s) => s.day === day && s.slot === slot) ? "free" : "off";
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
                          state === "off" && "border border-dashed border-line",
                        )}
                      >
                        {state === "blocked" && (
                          <span className="flex h-full items-center justify-center font-mono text-[10px] text-faint">✕</span>
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
      <WeekGrid bandId={bandId} />
    </div>
  );
}
