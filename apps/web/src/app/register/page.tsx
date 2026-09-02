"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Wordmark } from "@/components/wordmark";
import { registerAccount } from "@/lib/auth";

export default function RegisterPage() {
  return (
    <Suspense>
      <RegisterForm />
    </Suspense>
  );
}

function RegisterForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const data = new FormData(event.currentTarget);
    const result = await registerAccount(
      String(data.get("email")),
      String(data.get("password")),
      String(data.get("displayName")),
    );
    if (result.ok) {
      const next = searchParams.get("next");
      router.push(next && next.startsWith("/") ? next : "/home");
      router.refresh();
      return;
    }
    setError(result.detail);
    setBusy(false);
  }

  return (
    <main className="flex min-h-dvh items-center justify-center p-6">
      <div className="w-full max-w-sm space-y-8">
        <div className="space-y-2">
          <Wordmark />
          <p className="text-sm text-muted">Create your account — your band is one invite link away.</p>
        </div>
        <form onSubmit={onSubmit} className="space-y-4">
          <Field label="Your name" htmlFor="displayName">
            <Input id="displayName" name="displayName" autoComplete="name" placeholder="Lasse" required />
          </Field>
          <Field label="Email" htmlFor="email">
            <Input id="email" name="email" type="email" autoComplete="email" required />
          </Field>
          <Field label="Password" htmlFor="password">
            <Input
              id="password"
              name="password"
              type="password"
              autoComplete="new-password"
              minLength={10}
              placeholder="At least 10 characters"
              required
            />
          </Field>
          {error && <p className="text-sm text-accent">{error}</p>}
          <Button type="submit" disabled={busy} className="w-full">
            {busy ? "Creating…" : "Create account"}
          </Button>
        </form>
        <p className="text-sm text-muted">
          Already playing?{" "}
          <Link href="/login" className="font-medium text-ink underline underline-offset-4">
            Sign in
          </Link>
        </p>
      </div>
    </main>
  );
}
