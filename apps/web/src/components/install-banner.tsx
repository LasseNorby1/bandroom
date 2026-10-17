"use client";

import { Share } from "lucide-react";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { canPromptInstall, dismissInstall, installDismissed, isIos, isPhone, isStandalone, onInstallChange, promptInstall } from "@/lib/install";

/**
 * Shown once on a phone that isn't running from the home screen: the step that
 * makes notifications possible on iOS and makes the app feel like one everywhere.
 * Dismissable per device; gone by itself once installed.
 */
export function InstallBanner() {
  const [show, setShow] = useState(false);
  const [canPrompt, setCanPrompt] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const refresh = () => {
      setShow(isPhone() && !isStandalone() && !installDismissed());
      setCanPrompt(canPromptInstall());
    };
    refresh();
    return onInstallChange(refresh);
  }, []);

  if (!show) return null;

  const ios = isIos();

  async function install() {
    setBusy(true);
    try {
      if (await promptInstall()) {
        setShow(false);
      }
    } finally {
      setBusy(false);
    }
  }

  function notNow() {
    dismissInstall();
    setShow(false);
  }

  return (
    <div className="mb-5 space-y-3 rounded-xl border border-accent/30 bg-accent-soft px-5 py-4" role="region" aria-label="Add to home screen">
      <div>
        <p className="text-sm font-medium">Put Bandroom on your home screen</p>
        <p className="text-sm text-muted">
          {ios
            ? "That's what makes notifications possible on iPhone, and it opens like an app."
            : "Opens like an app, and proposals arrive as notifications."}
        </p>
      </div>
      {ios ? (
        <ol className="space-y-1 text-sm">
          <li className="flex gap-2">
            <span className="font-mono text-[11px] leading-5 text-faint">1</span>
            <span>Tap <Share className="inline size-4 align-text-bottom text-ink" aria-label="Share" /> <span className="font-medium">Share</span> at the bottom of Safari</span>
          </li>
          <li className="flex gap-2">
            <span className="font-mono text-[11px] leading-5 text-faint">2</span>
            <span>Choose <span className="font-medium">Add to Home Screen</span>, then open Bandroom from there</span>
          </li>
        </ol>
      ) : !canPrompt ? (
        <p className="text-sm">In your browser&apos;s menu, choose <span className="font-medium">Add to Home screen</span> or <span className="font-medium">Install app</span>.</p>
      ) : null}
      <div className="flex items-center gap-2">
        {!ios && canPrompt && (
          <Button type="button" size="sm" disabled={busy} onClick={() => void install()}>
            {busy ? "…" : "Add to home screen"}
          </Button>
        )}
        <Button type="button" size="sm" variant="ghost" onClick={notNow}>
          {ios || !canPrompt ? "Got it" : "Not now"}
        </Button>
      </div>
    </div>
  );
}
