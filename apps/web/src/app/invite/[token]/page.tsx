"use client";

import Link from "next/link";
import { use, useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Wordmark } from "@/components/wordmark";
import { api } from "@/lib/api";
import { refreshSession } from "@/lib/auth";
import type { Schemas } from "@/lib/types";

type State =
  | { kind: "checking" }
  | { kind: "needsAccount" }
  | { kind: "joining" }
  | { kind: "joined"; result: Schemas["AcceptInviteResponse"] }
  | { kind: "invalid" };

export default function InvitePage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = use(params);
  const [state, setState] = useState<State>({ kind: "checking" });

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const session = await refreshSession();
      if (cancelled) return;
      if (!session) {
        setState({ kind: "needsAccount" });
        return;
      }

      setState({ kind: "joining" });
      const { data, error } = await api.POST("/invites/{token}/accept", {
        params: { path: { token } },
      });
      if (cancelled) return;
      setState(error || !data ? { kind: "invalid" } : { kind: "joined", result: data });
    })();

    return () => {
      cancelled = true;
    };
  }, [token]);

  const next = `/invite/${token}`;

  return (
    <main className="flex min-h-dvh items-center justify-center p-6">
      <div className="w-full max-w-sm space-y-6">
        <Wordmark />

        {(state.kind === "checking" || state.kind === "joining") && (
          <p className="text-sm text-muted">
            {state.kind === "checking" ? "One moment…" : "Joining the band…"}
          </p>
        )}

        {state.kind === "needsAccount" && (
          <div className="space-y-4">
            <div>
              <h1 className="font-display text-xl font-semibold tracking-tight">
                You&apos;ve been invited to a band
              </h1>
              <p className="text-sm text-muted">Create an account (under a minute) to join.</p>
            </div>
            <div className="flex gap-2">
              <Link href={`/register?next=${encodeURIComponent(next)}`}>
                <Button>Create account</Button>
              </Link>
              <Link href={`/login?next=${encodeURIComponent(next)}`}>
                <Button variant="outline">I have one</Button>
              </Link>
            </div>
          </div>
        )}

        {state.kind === "joined" && (
          <div className="space-y-4">
            <div>
              <p className="font-mono text-xs tracking-wider text-accent">welcome</p>
              <h1 className="font-display text-xl font-semibold tracking-tight">
                You&apos;re in {state.result.bandName}
              </h1>
              <p className="text-sm text-muted">First stop: set your weekly pattern.</p>
            </div>
            <Link href={`/b/${state.result.bandId}/availability`}>
              <Button>Open the band</Button>
            </Link>
          </div>
        )}

        {state.kind === "invalid" && (
          <div className="space-y-3">
            <h1 className="font-display text-xl font-semibold tracking-tight">
              This invite doesn&apos;t work anymore
            </h1>
            <p className="text-sm text-muted">
              It may have expired, been revoked, or used up. Ask for a fresh link.
            </p>
          </div>
        )}
      </div>
    </main>
  );
}
