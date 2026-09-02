"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import QRCode from "qrcode";
import { use, useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useInvites, useMyAvailability } from "@/lib/band-hooks";
import { formatDay } from "@/lib/format";
import type { InviteCreated, PracticeSlot } from "@/lib/types";

function CopyButton({ value, label = "Copy" }: { value: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      variant="outline"
      size="sm"
      onClick={async () => {
        await navigator.clipboard.writeText(value);
        setCopied(true);
        setTimeout(() => setCopied(false), 1500);
      }}
    >
      {copied ? "Copied" : label}
    </Button>
  );
}

function InviteQr({ url }: { url: string }) {
  const [dataUrl, setDataUrl] = useState<string | null>(null);

  useEffect(() => {
    QRCode.toDataURL(url, { width: 220, margin: 1 })
      .then(setDataUrl)
      .catch(() => setDataUrl(null));
  }, [url]);

  if (!dataUrl) return null;
  // eslint-disable-next-line @next/next/no-img-element -- data url
  return <img src={dataUrl} alt="Invite QR code" className="rounded-lg border border-line" />;
}

function Invites({ bandId }: { bandId: string }) {
  const { isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const invites = useInvites(bandId);
  const [created, setCreated] = useState<InviteCreated | null>(null);

  const create = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/invites", {
        params: { path: { bandId } },
        body: { maxUses: null, expiresInDays: null },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: (invite) => {
      setCreated(invite);
      void queryClient.invalidateQueries({ queryKey: bandKeys.invites(bandId) });
    },
  });

  const revoke = useMutation({
    mutationFn: async (inviteId: string) => {
      const { error } = await api.DELETE("/bands/{bandId}/invites/{inviteId}", {
        params: { path: { bandId, inviteId } },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      setCreated(null);
      void queryClient.invalidateQueries({ queryKey: bandKeys.invites(bandId) });
    },
  });

  if (!isAdmin) {
    return null;
  }

  const inviteUrl = created ? `${window.location.origin}/invite/${created.token}` : null;

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Invite the band</h2>
        <p className="text-sm text-muted">
          One link joins the band — send it, or hold the QR up in the rehearsal room.
        </p>
      </div>

      {inviteUrl ? (
        <div className="space-y-3 rounded-lg border border-ok/40 bg-ok-soft/50 p-4">
          <p className="text-xs text-muted">
            This link is shown once — anyone with it can join until it expires.
          </p>
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 truncate rounded bg-paper px-2 py-1.5 font-mono text-xs">
              {inviteUrl}
            </code>
            <CopyButton value={inviteUrl} />
          </div>
          <InviteQr url={inviteUrl} />
        </div>
      ) : (
        <Button onClick={() => create.mutate()} disabled={create.isPending}>
          {create.isPending ? "Creating…" : "Create invite link"}
        </Button>
      )}

      {invites.data && invites.data.length > 0 && (
        <ul className="space-y-2">
          {invites.data.map((invite) => (
            <li
              key={invite.id}
              className="flex items-center justify-between rounded-lg border border-line px-3.5 py-2.5 text-sm"
            >
              <span className="text-muted">
                expires {formatDay(invite.expiresAt.slice(0, 10))} · used {invite.useCount}
                {invite.maxUses != null && `/${invite.maxUses}`}
              </span>
              <button
                type="button"
                onClick={() => revoke.mutate(invite.id)}
                className="text-xs text-muted hover:text-accent"
              >
                revoke
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function CalendarFeed({ bandId }: { bandId: string }) {
  const mine = useMyAvailability(bandId);
  if (!mine.data) return null;

  const apiBase = process.env.NEXT_PUBLIC_API_URL ?? "/api";
  const feedUrl = apiBase.startsWith("http")
    ? `${apiBase}${mine.data.calendarFeedPath}`
    : `${window.location.origin}${apiBase}${mine.data.calendarFeedPath}`;

  return (
    <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Your calendar feed</h2>
        <p className="text-sm text-muted">
          Subscribe once and band events appear in the calendar you already live in. Proposed
          practices show as tentative.
        </p>
      </div>
      <div className="flex items-center gap-2">
        <code className="min-w-0 flex-1 truncate rounded bg-paper px-2 py-1.5 font-mono text-xs">
          {feedUrl}
        </code>
        <CopyButton value={feedUrl} label="Copy url" />
      </div>
      <p className="text-xs text-faint">
        Google Calendar: Other calendars → + → From URL. Apple Calendar: File → New Calendar
        Subscription. This url is personal — treat it like a password.
      </p>
    </section>
  );
}

function BandSettings({ bandId }: { bandId: string }) {
  const { detail, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(false);

  const update = useMutation({
    mutationFn: async (body: {
      name: string | null;
      timeZone: string | null;
      quorum: number | null;
      defaultPracticeSlot: PracticeSlot | null;
      rehearsalSpace: string | null;
    }) => {
      const { data, error } = await api.PATCH("/bands/{bandId}", {
        params: { path: { bandId } },
        body,
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setSaved(true);
      setTimeout(() => setSaved(false), 1500);
      void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
    },
  });

  if (!detail) return null;
  const band = detail.band;

  if (!isAdmin) {
    return (
      <section className="space-y-2 rounded-xl border border-line bg-surface p-5 text-sm">
        <h2 className="font-medium">Band settings</h2>
        <p className="text-muted">
          {band.timeZone} · quorum {band.quorum} · default {band.defaultPracticeSlot}
          {band.rehearsalSpace && ` · ${band.rehearsalSpace}`}
        </p>
        <p className="text-xs text-faint">Only admins can change these.</p>
      </section>
    );
  }

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <h2 className="font-medium">Band settings</h2>
      <form
        className="grid gap-4 sm:grid-cols-2"
        onSubmit={(event) => {
          event.preventDefault();
          const data = new FormData(event.currentTarget);
          update.mutate({
            name: String(data.get("name")) || null,
            timeZone: String(data.get("timeZone")) || null,
            quorum: Number(data.get("quorum")) || null,
            defaultPracticeSlot: (String(data.get("slot")) as PracticeSlot) || null,
            rehearsalSpace: String(data.get("rehearsalSpace")),
          });
        }}
      >
        <Field label="Band name" htmlFor="name">
          <Input id="name" name="name" defaultValue={band.name} maxLength={100} required />
        </Field>
        <Field label="Timezone (IANA)" htmlFor="timeZone">
          <Input id="timeZone" name="timeZone" defaultValue={band.timeZone} list="timezones" />
        </Field>
        <datalist id="timezones">
          {["Europe/Copenhagen", "Europe/Oslo", "Europe/Stockholm", "Europe/Berlin", "Europe/London", "America/New_York", "America/Los_Angeles"].map(
            (zone) => (
              <option key={zone} value={zone} />
            ),
          )}
        </datalist>
        <Field label="Quorum (practice happens at)" htmlFor="quorum">
          <Input
            id="quorum"
            name="quorum"
            type="number"
            min={1}
            max={50}
            defaultValue={band.quorum}
          />
        </Field>
        <Field label="Default slot" htmlFor="slot">
          <select
            id="slot"
            name="slot"
            defaultValue={band.defaultPracticeSlot}
            className="h-10 w-full rounded-lg border border-line bg-surface px-3 text-sm"
          >
            <option value="evening">evening · 19–22</option>
            <option value="afternoon">afternoon · 14–17</option>
          </select>
        </Field>
        <Field label="Rehearsal space" htmlFor="rehearsalSpace" className="sm:col-span-2">
          <Input
            id="rehearsalSpace"
            name="rehearsalSpace"
            defaultValue={band.rehearsalSpace ?? ""}
            maxLength={200}
            placeholder="Møllegade 3, kælderen"
          />
        </Field>
        <div className="flex items-center gap-3 sm:col-span-2">
          <Button type="submit" disabled={update.isPending}>
            {update.isPending ? "Saving…" : "Save settings"}
          </Button>
          {saved && <p className="text-xs text-ok">Saved.</p>}
          {update.isError && <p className="text-xs text-accent">Couldn&apos;t save.</p>}
        </div>
      </form>
    </section>
  );
}

export default function SettingsPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);

  return (
    <div className="space-y-6">
      <Invites bandId={bandId} />
      <CalendarFeed bandId={bandId} />
      <BandSettings bandId={bandId} />
    </div>
  );
}
