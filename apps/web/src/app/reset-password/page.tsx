"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Wordmark } from "@/components/wordmark";
import { api } from "@/lib/api";

export default function ResetPasswordPage() {
  return (
    <Suspense>
      <ResetPasswordForm />
    </Suspense>
  );
}

function ResetPasswordForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const email = searchParams.get("email") ?? "";
  const token = searchParams.get("token") ?? "";
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const linkBroken = !email || !token;

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const data = new FormData(event.currentTarget);
    const { error: problem } = await api.POST("/auth/reset-password", {
      body: { email, token, newPassword: String(data.get("newPassword")) },
    });
    if (problem) {
      const messages = problem.errors ? Object.values(problem.errors).flat() : [];
      setError(messages.join(" ") || "That didn't work — request a new link.");
      setBusy(false);
      return;
    }
    router.push("/login");
  }

  return (
    <main className="flex min-h-dvh items-center justify-center p-6">
      <div className="w-full max-w-sm space-y-8">
        <div className="space-y-2">
          <Wordmark />
          <p className="text-sm text-muted">
            {linkBroken ? "This reset link is incomplete." : `Set a new password for ${email}.`}
          </p>
        </div>
        {linkBroken ? (
          <Link href="/forgot-password" className="inline-block">
            <Button variant="outline">Request a new link</Button>
          </Link>
        ) : (
          <form onSubmit={onSubmit} className="space-y-4">
            <Field label="New password" htmlFor="newPassword">
              <Input
                id="newPassword"
                name="newPassword"
                type="password"
                autoComplete="new-password"
                minLength={10}
                placeholder="At least 10 characters"
                required
              />
            </Field>
            {error && <p className="text-sm text-accent">{error}</p>}
            <Button type="submit" disabled={busy} className="w-full">
              {busy ? "Saving…" : "Set new password"}
            </Button>
          </form>
        )}
      </div>
    </main>
  );
}
