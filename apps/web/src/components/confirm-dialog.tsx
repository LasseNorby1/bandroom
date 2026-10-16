"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";

/**
 * A yes/no gate in front of something that can't be undone. `children` is
 * the trigger; `extra` renders inside the dialog (e.g. a password field) and
 * is read by onConfirm through the form.
 */
export function ConfirmDialog({
  title,
  description,
  confirmLabel,
  children,
  extra,
  onConfirm,
}: {
  title: string;
  description: string;
  confirmLabel: string;
  children: React.ReactNode;
  extra?: React.ReactNode;
  onConfirm: (form: FormData) => Promise<string | null>;
}) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  return (
    <Dialog open={open} onOpenChange={(next) => { setOpen(next); setError(null); }}>
      <DialogTrigger asChild>{children}</DialogTrigger>
      <DialogContent>
        <form
          className="space-y-4"
          onSubmit={async (event) => {
            event.preventDefault();
            setBusy(true);
            setError(null);
            const problem = await onConfirm(new FormData(event.currentTarget));
            setBusy(false);
            if (problem) {
              setError(problem);
            } else {
              setOpen(false);
            }
          }}
        >
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
            <DialogDescription>{description}</DialogDescription>
          </DialogHeader>
          {extra}
          {error && <p className="text-sm text-accent">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)} disabled={busy}>
              Keep it
            </Button>
            <Button type="submit" disabled={busy} className="bg-accent text-white hover:opacity-90">
              {busy ? "…" : confirmLabel}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
