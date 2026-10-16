"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import QRCode from "qrcode";
import { use, useEffect, useState } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useInvites, useMyAvailability } from "@/lib/band-hooks";
import { formatDay } from "@/lib/format";
import { formatBytes } from "@/lib/upload";
import { cn } from "@/lib/cn";
import type { BandRole, InviteCreated, IsoDay, PracticeSlot } from "@/lib/types";
import { DAY_ORDER } from "@/lib/format";

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

function Members({ bandId }: { bandId: string }) {
  const { detail, me, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const router = useRouter();

  const invalidate = () => queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });

  const setRole = useMutation({
    mutationFn: async ({ membershipId, role }: { membershipId: string; role: BandRole }) => {
      const { error } = await api.PATCH("/bands/{bandId}/members/{membershipId}", {
        params: { path: { bandId, membershipId } },
        body: { role },
      });
      if (error) throw error;
    },
    onSuccess: () => void invalidate(),
  });

  if (!detail) return null;
  const admins = detail.members.filter((m) => m.role === "admin").length;

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Members</h2>
        <p className="text-sm text-muted">
          {detail.members.length} in the room. Leaving keeps what you made here; the band just stops seeing you.
        </p>
      </div>
      <ul className="divide-y divide-line rounded-lg border border-line">
        {detail.members.map((member) => {
          const self = member.membershipId === me?.membershipId;
          const lastAdmin = member.role === "admin" && admins === 1;
          return (
            <li key={member.membershipId} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3.5 py-2.5 text-sm">
              <span className="font-medium">{member.displayName}</span>
              {self && <span className="font-mono text-[10px] text-faint">you</span>}
              <span
                className={cn(
                  "rounded-full border px-2 py-0.5 font-mono text-[10px]",
                  member.role === "admin" ? "border-ink text-ink" : "border-line text-muted",
                )}
              >
                {member.role}
              </span>
              <span className="ml-auto flex items-center gap-3">
                {isAdmin && !self && (
                  <button
                    type="button"
                    disabled={setRole.isPending || lastAdmin}
                    onClick={() =>
                      setRole.mutate({ membershipId: member.membershipId, role: member.role === "admin" ? "member" : "admin" })
                    }
                    className="text-xs text-muted hover:text-ink disabled:opacity-40"
                  >
                    {member.role === "admin" ? "make member" : "make admin"}
                  </button>
                )}
                {isAdmin && !self && (
                  <ConfirmDialog
                    title={`Remove ${member.displayName}?`}
                    description="They lose access to the room. Their takes, comments and events stay."
                    confirmLabel="Remove"
                    onConfirm={async () => {
                      const { error } = await api.DELETE("/bands/{bandId}/members/{membershipId}", {
                        params: { path: { bandId, membershipId: member.membershipId } },
                      });
                      if (error) return "Couldn't remove them.";
                      await invalidate();
                      return null;
                    }}
                  >
                    <button type="button" className="text-xs text-muted hover:text-accent">remove</button>
                  </ConfirmDialog>
                )}
                {self && (
                  <ConfirmDialog
                    title="Leave the band?"
                    description={
                      detail.members.length === 1
                        ? "You're the last one here — leaving deletes the band and everything in it."
                        : lastAdmin
                          ? "You're the only admin. The longest-standing member becomes admin when you go."
                          : "You lose access to the room. What you made here stays. An invite link gets you back in."
                    }
                    confirmLabel={detail.members.length === 1 ? "Delete band" : "Leave"}
                    onConfirm={async () => {
                      const { error } = await api.DELETE("/bands/{bandId}/members/me", { params: { path: { bandId } } });
                      if (error) return "Couldn't leave.";
                      await queryClient.invalidateQueries({ queryKey: ["bands"] });
                      router.push("/home");
                      return null;
                    }}
                  >
                    <button type="button" className="text-xs text-muted hover:text-accent">leave band</button>
                  </ConfirmDialog>
                )}
              </span>
            </li>
          );
        })}
      </ul>
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
      reminderHour: number | null;
      recurringPracticeDay: IsoDay | null;
      clearRecurringPracticeDay: boolean;
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
          {band.timeZone} · quorum {band.quorum} · default {band.defaultPracticeSlot} · reminders at{" "}
          {String(band.reminderHour).padStart(2, "0")}:00
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
            reminderHour: Number(data.get("reminderHour")),
            recurringPracticeDay: (String(data.get("recurringDay")) as IsoDay) || null,
            clearRecurringPracticeDay: !data.get("recurringDay"),
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
        <Field label="Reminder the day before, at" htmlFor="reminderHour">
          <select
            id="reminderHour"
            name="reminderHour"
            defaultValue={band.reminderHour}
            className="h-10 w-full rounded-lg border border-line bg-surface px-3 text-sm"
          >
            {Array.from({ length: 24 }, (_, hour) => (
              <option key={hour} value={hour}>{String(hour).padStart(2, "0")}:00</option>
            ))}
          </select>
        </Field>
        <Field label="Standing practice day" htmlFor="recurringDay">
          <select
            id="recurringDay"
            name="recurringDay"
            defaultValue={band.recurringPracticeDay ?? ""}
            className="h-10 w-full rounded-lg border border-line bg-surface px-3 text-sm"
          >
            <option value="">none — the finder proposes</option>
            {DAY_ORDER.map((day) => (
              <option key={day} value={day}>every {day}</option>
            ))}
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

function PlanAndStorage({ bandId }: { bandId: string }) {
  const { detail, isAdmin } = useBandContext();
  const billing = useQuery({
    queryKey: ["band", bandId, "billing"],
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/billing", { params: { path: { bandId } } });
      if (error) throw error;
      return data;
    },
  });
  if (!detail) return null;
  const band = detail.band;
  const usedFraction = band.storageQuotaBytes > 0 ? band.storageUsedBytes / band.storageQuotaBytes : 0;

  return (
    <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
      <div className="flex items-baseline justify-between">
        <h2 className="font-medium">Plan &amp; storage</h2>
        <span className="rounded-full border border-line px-2.5 py-0.5 font-mono text-[11px] text-muted">
          {band.plan}
        </span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-paper">
        <div
          className="h-full rounded-full bg-ink"
          style={{ width: `${Math.min(100, Math.round(usedFraction * 100))}%` }}
        />
      </div>
      <p className="font-mono text-[11px] text-muted">
        {formatBytes(band.storageUsedBytes)} of {formatBytes(band.storageQuotaBytes)} — demos and stems
      </p>
      {billing.data && (
        <div className="flex flex-wrap items-center gap-3 border-t border-line pt-3">
          {billing.data.isPro ? (
            <>
              <p className="text-sm text-muted">
                Pro: 25 GB and ai polish.
                {billing.data.planExpiresAt && ` Renews around ${formatDay(billing.data.planExpiresAt.slice(0, 10))}.`}
              </p>
              {billing.data.portalUrl && isAdmin && (
                <a href={billing.data.portalUrl} target="_blank" rel="noopener" className="text-sm underline underline-offset-4 hover:text-ink">
                  Manage subscription
                </a>
              )}
            </>
          ) : (
            <>
              <p className="text-sm text-muted">Free: 1 GB. Pro adds 25 GB and the ai mix &amp; master, one payment covers the band.</p>
              {billing.data.checkoutUrl ? (
                <a href={billing.data.checkoutUrl} className="inline-flex h-9 items-center rounded-full bg-ink px-4 text-sm font-medium text-paper hover:opacity-90">
                  Upgrade to Pro
                </a>
              ) : isAdmin ? (
                <p className="text-xs text-faint">Checkout isn&apos;t open yet.</p>
              ) : null}
            </>
          )}
        </div>
      )}
    </section>
  );
}

function AiSwitch({ bandId }: { bandId: string }) {
  const { detail, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const toggle = useMutation({
    mutationFn: async (aiEnabled: boolean) => {
      const { error } = await api.PATCH("/bands/{bandId}", {
        params: { path: { bandId } },
        body: { name: null, timeZone: null, quorum: null, defaultPracticeSlot: null, rehearsalSpace: null, reminderHour: null, recurringPracticeDay: null, clearRecurringPracticeDay: false, aiEnabled },
      });
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) }),
  });
  if (!detail || !detail.band.aiAvailable) return null;
  const on = detail.band.aiEnabled;
  return (
    <section className="flex items-start justify-between gap-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">The roadie (ai)</h2>
        <p className="text-sm text-muted">
          Parses &ldquo;can&apos;t the next two weekends&rdquo; into blockouts, drafts a gig from a venue email, and writes
          the catch-up. Never touches the music. Your audio is never used to train anything.
        </p>
      </div>
      {isAdmin ? (
        <button
          type="button"
          role="switch"
          aria-checked={on}
          disabled={toggle.isPending}
          onClick={() => toggle.mutate(!on)}
          className={cn("shrink-0 rounded-full border px-3 py-1 font-mono text-[11px]", on ? "border-ok bg-ok-soft text-ok" : "border-line text-muted")}
        >
          {on ? "on" : "off"}
        </button>
      ) : (
        <span className="font-mono text-[11px] text-muted">{on ? "on" : "off"}</span>
      )}
    </section>
  );
}

export default function SettingsPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);

  return (
    <div className="space-y-6">
      <Invites bandId={bandId} />
      <Members bandId={bandId} />
      <PlanAndStorage bandId={bandId} />
      <AiSwitch bandId={bandId} />
      <CalendarFeed bandId={bandId} />
      <BandSettings bandId={bandId} />
    </div>
  );
}
