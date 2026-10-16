"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { PushToggle } from "@/components/push-toggle";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { logout } from "@/lib/auth";
import { useMe } from "@/lib/band-hooks";

type Problem = { errors?: Record<string, string[]>; title?: string } | undefined;

function describe(error: unknown, fallback: string): string {
  const problem = error as Problem;
  if (problem?.errors) return Object.values(problem.errors).flat().join(" ");
  return problem?.title ?? fallback;
}

function ChangeEmail({ current }: { current: string }) {
  const [state, setState] = useState<{ busy: boolean; message: string | null; ok: boolean }>({ busy: false, message: null, ok: false });
  const router = useRouter();

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Email</h2>
        <p className="text-sm text-muted">Signed in as {current}.</p>
      </div>
      <form
        className="grid gap-4 sm:grid-cols-2"
        onSubmit={async (event) => {
          event.preventDefault();
          const form = event.currentTarget;
          const data = new FormData(form);
          setState({ busy: true, message: null, ok: false });
          const { error } = await api.POST("/auth/change-email", {
            body: { password: String(data.get("password")), newEmail: String(data.get("newEmail")) },
          });
          if (error) {
            setState({ busy: false, message: describe(error, "Couldn't change the email."), ok: false });
            return;
          }
          form.reset();
          setState({ busy: false, message: "Email changed.", ok: true });
          router.refresh();
        }}
      >
        <Field label="New email" htmlFor="newEmail">
          <Input id="newEmail" name="newEmail" type="email" autoComplete="email" required />
        </Field>
        <Field label="Your password" htmlFor="emailPassword">
          <Input id="emailPassword" name="password" type="password" autoComplete="current-password" required />
        </Field>
        <div className="flex items-center gap-3 sm:col-span-2">
          <Button type="submit" variant="outline" disabled={state.busy}>Change email</Button>
          {state.message && <p className={state.ok ? "text-xs text-ok" : "text-xs text-accent"}>{state.message}</p>}
        </div>
      </form>
    </section>
  );
}

function ChangePassword() {
  const router = useRouter();
  const [state, setState] = useState<{ busy: boolean; message: string | null }>({ busy: false, message: null });

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Password</h2>
        <p className="text-sm text-muted">Changing it signs you out everywhere, this device included.</p>
      </div>
      <form
        className="grid gap-4 sm:grid-cols-2"
        onSubmit={async (event) => {
          event.preventDefault();
          const data = new FormData(event.currentTarget);
          setState({ busy: true, message: null });
          const { error } = await api.POST("/auth/change-password", {
            body: { currentPassword: String(data.get("current")), newPassword: String(data.get("next")) },
          });
          if (error) {
            setState({ busy: false, message: describe(error, "Couldn't change the password.") });
            return;
          }
          await logout();
          router.push("/login");
          router.refresh();
        }}
      >
        <Field label="Current password" htmlFor="current">
          <Input id="current" name="current" type="password" autoComplete="current-password" required />
        </Field>
        <Field label="New password (10+ characters)" htmlFor="next">
          <Input id="next" name="next" type="password" autoComplete="new-password" minLength={10} required />
        </Field>
        <div className="flex items-center gap-3 sm:col-span-2">
          <Button type="submit" variant="outline" disabled={state.busy}>Change password</Button>
          {state.message && <p className="text-xs text-accent">{state.message}</p>}
        </div>
      </form>
    </section>
  );
}

function ExportAndDelete() {
  const router = useRouter();
  const [exporting, setExporting] = useState(false);

  return (
    <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
      <div>
        <h2 className="font-medium">Your data</h2>
        <p className="text-sm text-muted">
          Everything Bandroom holds about you, as one json file. Deleting your account removes your
          name and email; the takes and comments you left in your bands stay, credited to &ldquo;Deleted
          member&rdquo;.
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Button
          variant="outline"
          disabled={exporting}
          onClick={async () => {
            setExporting(true);
            const { data } = await api.GET("/auth/me/export");
            setExporting(false);
            if (!data) return;
            const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = `bandroom-export-${new Date().toISOString().slice(0, 10)}.json`;
            a.click();
            URL.revokeObjectURL(url);
          }}
        >
          {exporting ? "Preparing…" : "Download my data"}
        </Button>
        <ConfirmDialog
          title="Delete your account?"
          description="This can't be undone. You leave every band; your name and email are removed."
          confirmLabel="Delete account"
          extra={
            <Field label="Your password" htmlFor="deletePassword">
              <Input id="deletePassword" name="password" type="password" autoComplete="current-password" required />
            </Field>
          }
          onConfirm={async (form) => {
            const { error } = await api.DELETE("/auth/me", { body: { password: String(form.get("password")) } });
            if (error) return describe(error, "Couldn't delete the account.");
            await logout();
            router.push("/login");
            return null;
          }}
        >
          <Button variant="ghost" className="text-accent">Delete account</Button>
        </ConfirmDialog>
      </div>
    </section>
  );
}

export default function AccountPage() {
  const me = useMe();

  if (me.isPending) {
    return <p className="text-sm text-muted">Loading…</p>;
  }

  if (me.isError || !me.data) {
    return <p className="text-sm text-accent">Couldn&apos;t load your account.</p>;
  }

  return (
    <div className="space-y-6">
      <div>
        <p className="font-mono text-xs tracking-wider text-accent">account</p>
        <h1 className="font-display text-2xl font-semibold tracking-tight">{me.data.displayName}</h1>
      </div>
      <section className="space-y-3 rounded-xl border border-line bg-surface p-5">
        <div>
          <h2 className="font-medium">Notifications</h2>
          <p className="text-sm text-muted">
            The whole point of the finder is that &ldquo;Thursday works for everyone&rdquo; reaches you. One switch per device.
          </p>
        </div>
        <PushToggle />
      </section>
      <ChangeEmail current={me.data.email} />
      <ChangePassword />
      <ExportAndDelete />
    </div>
  );
}
