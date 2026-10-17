"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useSongs } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import { formatDuration, formatMinutes } from "@/lib/duration";
import type { EventDetail, EventItem, SetlistItem } from "@/lib/types";

const field = "h-9 rounded-lg border border-line bg-paper px-3 text-sm placeholder:text-faint";

/** The one person to call on the day. Creator/admin can edit; everyone can tap to call. */
export function GigContact({ bandId, eventId, contact, mayEdit }: { bandId: string; eventId: string; contact: EventDetail["contact"]; mayEdit: boolean }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState({ name: contact.name ?? "", phone: contact.phone ?? "", email: contact.email ?? "" });

  const save = useMutation({
    mutationFn: async () => {
      const { error } = await api.PATCH("/bands/{bandId}/events/{eventId}", {
        params: { path: { bandId, eventId } },
        body: { contactName: draft.name, contactPhone: draft.phone, contactEmail: draft.email },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      setEditing(false);
      void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) });
    },
  });

  const empty = !contact.name && !contact.phone && !contact.email;
  if (!editing) {
    if (empty && !mayEdit) return null;
    return (
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
        <span className="font-mono text-[11px] tracking-wider text-muted">contact</span>
        {empty ? (
          <span className="text-muted">nobody yet</span>
        ) : (
          <>
            {contact.name && <span className="font-medium">{contact.name}</span>}
            {contact.phone && <a href={`tel:${contact.phone.replace(/\s+/g, "")}`} className="underline decoration-line underline-offset-2 hover:text-ink">{contact.phone}</a>}
            {contact.email && <a href={`mailto:${contact.email}`} className="underline decoration-line underline-offset-2 hover:text-ink">{contact.email}</a>}
          </>
        )}
        {mayEdit && (
          <button type="button" onClick={() => setEditing(true)} className="font-mono text-[11px] text-muted hover:text-ink">
            {empty ? "add" : "edit"}
          </button>
        )}
      </div>
    );
  }

  return (
    <form
      className="flex flex-wrap items-center gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate();
      }}
    >
      <input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} placeholder="name" maxLength={100} className={field} aria-label="Contact name" />
      <input value={draft.phone} onChange={(e) => setDraft({ ...draft, phone: e.target.value })} placeholder="phone" maxLength={40} className={field} aria-label="Contact phone" />
      <input value={draft.email} onChange={(e) => setDraft({ ...draft, email: e.target.value })} placeholder="email" maxLength={200} className={field} aria-label="Contact email" />
      <Button type="submit" size="sm" disabled={save.isPending}>Save</Button>
      <Button type="button" size="sm" variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
      {save.isError && <span className="text-xs text-accent">Couldn&apos;t save — check the email.</span>}
    </form>
  );
}

/** "Who brings what": a line per item, a name, a tick. The list people otherwise write in comments. */
export function WhoBringsWhat({ bandId, eventId, items }: { bandId: string; eventId: string; items: EventItem[] }) {
  const { me, detail } = useBandContext();
  const queryClient = useQueryClient();
  const [text, setText] = useState("");
  const invalidate = () => void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) });

  const add = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/bands/{bandId}/events/{eventId}/items", {
        params: { path: { bandId, eventId } },
        body: { text: text.trim(), assigneeMembershipId: null },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      setText("");
      invalidate();
    },
  });

  const update = useMutation({
    mutationFn: async ({ item, body }: { item: EventItem; body: { assigneeMembershipId?: string | null; clearAssignee?: boolean; done?: boolean } }) => {
      const { error } = await api.PATCH("/bands/{bandId}/events/{eventId}/items/{itemId}", {
        params: { path: { bandId, eventId, itemId: item.id } },
        body: { text: null, assigneeMembershipId: body.assigneeMembershipId ?? null, clearAssignee: body.clearAssignee ?? false, done: body.done ?? null },
      });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: async (item: EventItem) => {
      const { error } = await api.DELETE("/bands/{bandId}/events/{eventId}/items/{itemId}", {
        params: { path: { bandId, eventId, itemId: item.id } },
      });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  const members = detail?.members ?? [];

  return (
    <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
      <p className="font-mono text-xs tracking-wider text-muted">who brings what</p>
      {items.length > 0 && (
        <ul className="divide-y divide-line">
          {items.map((item) => (
            <li key={item.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm">
              <label className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={item.done}
                  onChange={(e) => update.mutate({ item, body: { done: e.target.checked } })}
                  aria-label={`${item.text} done`}
                />
                <span className={cn(item.done && "text-faint line-through")}>{item.text}</span>
              </label>
              <select
                value={item.assigneeMembershipId ?? ""}
                onChange={(e) => update.mutate({ item, body: e.target.value ? { assigneeMembershipId: e.target.value } : { clearAssignee: true } })}
                className="h-7 rounded-md border border-line bg-paper px-2 font-mono text-[11px] text-muted"
                aria-label={`Who brings ${item.text}`}
              >
                <option value="">— nobody yet</option>
                {members.map((m) => (
                  <option key={m.membershipId} value={m.membershipId}>{m.membershipId === me?.membershipId ? "me" : m.displayName}</option>
                ))}
              </select>
              <button type="button" onClick={() => remove.mutate(item)} className="ml-auto font-mono text-[11px] text-faint hover:text-accent">remove</button>
            </li>
          ))}
        </ul>
      )}
      <form
        className="flex gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (text.trim()) add.mutate();
        }}
      >
        <input value={text} onChange={(e) => setText(e.target.value)} placeholder="small amp, cables, the merch box…" maxLength={200} className={cn(field, "flex-1")} aria-label="Item" />
        <Button type="submit" size="sm" variant="outline" disabled={add.isPending || !text.trim()}>Add</Button>
      </form>
    </section>
  );
}

/** A setlist on a gig only: songs from the repertoire in order, total from their durations, share as text. */
export function Setlist({ bandId, eventId, title, setlist, totalSeconds }: { bandId: string; eventId: string; title: string; setlist: SetlistItem[]; totalSeconds: number }) {
  const queryClient = useQueryClient();
  const songs = useSongs(bandId);
  const [copied, setCopied] = useState(false);

  const replace = useMutation({
    mutationFn: async (songIds: string[]) => {
      const { error } = await api.PUT("/bands/{bandId}/events/{eventId}/setlist", {
        params: { path: { bandId, eventId } },
        body: { songIds },
      });
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) }),
  });

  const ids = setlist.map((s) => s.songId);
  const move = (index: number, delta: number) => {
    const next = [...ids];
    const target = index + delta;
    if (target < 0 || target >= next.length) return;
    [next[index], next[target]] = [next[target], next[index]];
    replace.mutate(next);
  };
  const available = (songs.data ?? []).filter((s) => s.status === "active" && !ids.includes(s.id));
  const unknownLength = setlist.filter((s) => s.durationSeconds == null).length;

  async function share() {
    const lines = setlist.map((s, i) => `${i + 1}. ${s.title}${s.key ? ` (${s.key})` : ""}${s.durationSeconds != null ? ` · ${formatDuration(s.durationSeconds)}` : ""}`);
    const text = `${title}\n\n${lines.join("\n")}\n\n${totalSeconds > 0 ? `about ${formatMinutes(totalSeconds)}` : ""}`.trim();
    try {
      if (navigator.share) {
        await navigator.share({ title, text });
        return;
      }
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Cancelled share, or no clipboard: nothing to do.
    }
  }

  return (
    <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-mono text-xs tracking-wider text-muted">
          setlist{setlist.length > 0 && ` · ${setlist.length} songs`}
          {totalSeconds > 0 && ` · about ${formatMinutes(totalSeconds)}`}
          {unknownLength > 0 && totalSeconds > 0 && ` (+${unknownLength} without a length)`}
        </p>
        {setlist.length > 0 && (
          <button type="button" onClick={() => void share()} className="font-mono text-[11px] text-muted hover:text-ink">
            {copied ? "copied" : "share as text"}
          </button>
        )}
      </div>
      {setlist.length > 0 ? (
        <ol className="divide-y divide-line">
          {setlist.map((song, index) => (
            <li key={song.songId} className="flex items-center gap-3 py-2 text-sm">
              <span className="w-5 font-mono text-[11px] text-faint">{index + 1}</span>
              <span className="min-w-0 flex-1 truncate font-medium">{song.title}</span>
              <span className="font-mono text-[11px] text-muted">
                {song.key ?? ""}{song.durationSeconds != null && ` · ${formatDuration(song.durationSeconds)}`}
              </span>
              <span className="flex gap-1 font-mono text-[11px] text-muted">
                <button type="button" onClick={() => move(index, -1)} disabled={index === 0 || replace.isPending} aria-label="Move up" className="hover:text-ink disabled:opacity-30">↑</button>
                <button type="button" onClick={() => move(index, 1)} disabled={index === setlist.length - 1 || replace.isPending} aria-label="Move down" className="hover:text-ink disabled:opacity-30">↓</button>
                <button type="button" onClick={() => replace.mutate(ids.filter((id) => id !== song.songId))} disabled={replace.isPending} aria-label="Remove" className="hover:text-accent">×</button>
              </span>
            </li>
          ))}
        </ol>
      ) : (
        <p className="text-sm text-muted">No songs yet — pick from the repertoire below.</p>
      )}
      {available.length > 0 ? (
        <select
          value=""
          onChange={(e) => e.target.value && replace.mutate([...ids, e.target.value])}
          className="h-9 rounded-lg border border-line bg-paper px-3 text-sm"
          aria-label="Add a song"
        >
          <option value="">+ add a song</option>
          {available.map((s) => (
            <option key={s.id} value={s.id}>{s.title}{s.durationSeconds != null ? ` · ${formatDuration(s.durationSeconds)}` : ""}</option>
          ))}
        </select>
      ) : (
        songs.data && songs.data.length === 0 && <p className="text-xs text-faint">The repertoire is empty; add songs under Songs first.</p>
      )}
    </section>
  );
}
