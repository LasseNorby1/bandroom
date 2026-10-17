"use client";

import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";

/** A file picker dressed as a button, with transfer progress in the label. */
export function UploadButton({
  label,
  accept,
  busyLabel,
  onFile,
  variant = "primary",
}: {
  label: string;
  accept: string;
  busyLabel: string;
  /** Receives a progress setter so the button can show the transfer as it happens. */
  onFile: (file: File, onProgress: (fraction: number) => void) => Promise<void>;
  variant?: "primary" | "outline";
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState(false);

  return (
    <>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        className="hidden"
        onChange={async (event) => {
          const file = event.target.files?.[0];
          event.target.value = "";
          if (!file) return;
          setError(false);
          setProgress(0);
          try {
            await onFile(file, setProgress);
          } catch {
            setError(true);
          } finally {
            setProgress(null);
          }
        }}
      />
      <Button
        type="button"
        variant={variant}
        size="sm"
        disabled={progress !== null}
        onClick={() => inputRef.current?.click()}
      >
        {progress !== null ? `${busyLabel} ${Math.round(progress * 100)}%` : label}
      </Button>
      {error && <span className="text-xs text-accent">upload failed</span>}
    </>
  );
}
