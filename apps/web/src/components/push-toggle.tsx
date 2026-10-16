"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { currentState, disablePush, enablePush, type PushState } from "@/lib/push";

/** "Notify me on this phone": the one switch that makes rsvps arrive instead of waiting to be found. */
export function PushToggle({ compact = false }: { compact?: boolean }) {
  const [state, setState] = useState<PushState | "loading">("loading");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void currentState().then(setState);
  }, []);

  async function flip() {
    setBusy(true);
    setError(null);
    try {
      setState(state === "on" ? await disablePush() : await enablePush());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't change notifications.");
    } finally {
      setBusy(false);
    }
  }

  if (state === "loading") return null;

  if (state === "needs-install") {
    return (
      <p className="text-sm text-muted">
        On iPhone, notifications work once Bandroom is on your home screen: Share → <span className="font-medium text-ink">Add to Home Screen</span>, then open it from there.
      </p>
    );
  }

  if (state === "unsupported") {
    return <p className="text-sm text-muted">This browser can&apos;t do notifications. Use Chrome, Safari or Firefox on your phone.</p>;
  }

  if (state === "denied") {
    return <p className="text-sm text-muted">Notifications are blocked for bandroom.io in your browser settings. Allow them there, then come back.</p>;
  }

  return (
    <div className={compact ? "flex items-center gap-3" : "space-y-2"}>
      <Button type="button" size="sm" variant={state === "on" ? "outline" : "primary"} disabled={busy} onClick={flip}>
        {busy ? "…" : state === "on" ? "Notifications on · turn off" : "Notify me on this device"}
      </Button>
      {error && <span className="text-xs text-accent">{error}</span>}
      {!compact && state === "on" && (
        <p className="text-xs text-faint">Proposals arrive with “I&apos;m in” and “Can&apos;t” buttons; confirmations and the day-before reminder too. Nothing else.</p>
      )}
    </div>
  );
}
