"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { formatSeconds } from "@/lib/upload";

/** The mime the browser can actually record; Safari says mp4, the rest webm/opus. */
function pickMime(): { mime: string; ext: string } | null {
  if (typeof MediaRecorder === "undefined") return null;
  const candidates: [string, string][] = [
    ["audio/webm;codecs=opus", "webm"],
    ["audio/webm", "webm"],
    ["audio/mp4", "m4a"],
    ["audio/ogg;codecs=opus", "ogg"],
  ];
  for (const [mime, ext] of candidates) {
    if (MediaRecorder.isTypeSupported(mime)) return { mime, ext };
  }
  return null;
}

/**
 * Record a take right here (spec §5.2, "in-app recording"): one button, a
 * timer, stop → the file goes through the normal upload. No effects, no
 * editing — the voice memo app's job, done without leaving the room.
 */
export function Recorder({ onRecorded }: { onRecorded: (file: File) => Promise<void> }) {
  const [state, setState] = useState<"idle" | "recording" | "uploading" | "unsupported" | "denied">("idle");
  const [seconds, setSeconds] = useState(0);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<BlobPart[]>([]);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => () => {
    recorderRef.current?.stream.getTracks().forEach((track) => track.stop());
    if (timerRef.current) clearInterval(timerRef.current);
  }, []);

  async function start() {
    const picked = pickMime();
    if (!picked || !navigator.mediaDevices?.getUserMedia) {
      setState("unsupported");
      return;
    }
    let stream: MediaStream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch {
      setState("denied");
      return;
    }
    const recorder = new MediaRecorder(stream, { mimeType: picked.mime });
    chunksRef.current = [];
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunksRef.current.push(event.data);
    };
    recorder.onstop = async () => {
      stream.getTracks().forEach((track) => track.stop());
      if (timerRef.current) clearInterval(timerRef.current);
      const blob = new Blob(chunksRef.current, { type: picked.mime.split(";")[0] });
      const stamp = new Date().toISOString().slice(0, 16).replace("T", " ").replace(":", ".");
      const file = new File([blob], `recording ${stamp}.${picked.ext}`, { type: blob.type });
      setState("uploading");
      try {
        await onRecorded(file);
      } finally {
        setState("idle");
        setSeconds(0);
      }
    };
    recorderRef.current = recorder;
    recorder.start(1000);
    setSeconds(0);
    timerRef.current = setInterval(() => setSeconds((s) => s + 1), 1000);
    setState("recording");
  }

  function stop() {
    recorderRef.current?.stop();
  }

  if (state === "unsupported") {
    return <p className="text-xs text-faint">Recording isn&apos;t supported in this browser — upload a file instead.</p>;
  }

  return (
    <span className="inline-flex items-center gap-2">
      {state === "recording" ? (
        <Button type="button" size="sm" onClick={stop} className="bg-accent text-white hover:opacity-90">
          <span className="size-2 animate-pulse rounded-full bg-white" aria-hidden />
          Stop · {formatSeconds(seconds)}
        </Button>
      ) : (
        <Button type="button" size="sm" variant="outline" onClick={start} disabled={state === "uploading"}>
          {state === "uploading" ? "Uploading…" : "● Record a take"}
        </Button>
      )}
      {state === "denied" && <span className="text-xs text-accent">Microphone access was refused.</span>}
    </span>
  );
}
