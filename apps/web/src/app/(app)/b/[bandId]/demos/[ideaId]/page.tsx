"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useRef, useState } from "react";
import { DemoPlayer } from "@/components/demo-player";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { bandKeys, useIdea, useReferences } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import type { StemLabel } from "@/lib/types";
import { formatBytes, uploadDemoVersion, uploadReference, uploadStem } from "@/lib/upload";

const stemLabels: StemLabel[] = ["drums", "bass", "guitar", "keys", "vocals", "other"];

function UploadButton({
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

function ReferencePicker({
  id,
  label,
  defaultLabel,
  value,
  onChange,
  references,
  versions,
}: {
  id: string;
  label: string;
  defaultLabel: string;
  value: string;
  onChange: (value: string) => void;
  references: { id: string; title: string }[];
  versions: { id: string; number: string | number; kind: string; fileName: string }[];
}) {
  return (
    <label htmlFor={id} className="flex items-center gap-2">
      <span className="font-mono text-[11px] text-muted">{label}</span>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="h-9 rounded-lg border border-line bg-surface px-3 text-sm"
      >
        <option value="standard">{defaultLabel}</option>
        {references.length > 0 && (
          <optgroup label="match a reference">
            {references.map((ref) => (
              <option key={ref.id} value={`ref:${ref.id}`}>
                {ref.title}
              </option>
            ))}
          </optgroup>
        )}
        {versions.length > 0 && (
          <optgroup label="match one of our takes">
            {versions.map((version) => (
              <option key={version.id} value={`ver:${version.id}`}>
                v{version.number} — {version.kind === "aiMix" ? "AI demo mix" : version.fileName}
              </option>
            ))}
          </optgroup>
        )}
      </select>
    </label>
  );
}

export default function IdeaPage({
  params,
}: {
  params: Promise<{ bandId: string; ideaId: string }>;
}) {
  const { bandId, ideaId } = use(params);
  const queryClient = useQueryClient();
  const idea = useIdea(bandId, ideaId);
  const references = useReferences(bandId);
  const [mixReference, setMixReference] = useState("standard");
  const [masterReference, setMasterReference] = useState("standard");

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: bandKeys.demos(bandId) });
    void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
    void queryClient.invalidateQueries({ queryKey: ["band", bandId, "references"] });
  };

  const polish = useMutation({
    mutationFn: async () => {
      const refId = (value: string, kind: "ref" | "ver") =>
        value.startsWith(`${kind}:`) ? value.slice(4) : null;
      const { data, error } = await api.POST("/bands/{bandId}/song-ideas/{ideaId}/polish", {
        params: { path: { bandId, ideaId } },
        body: {
          referenceTrackId: refId(masterReference, "ref"),
          referenceVersionId: refId(masterReference, "ver"),
          mixReferenceTrackId: refId(mixReference, "ref"),
          mixReferenceVersionId: refId(mixReference, "ver"),
        },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: invalidate,
  });

  const deleteStem = useMutation({
    mutationFn: async (stemId: string) => {
      const { error } = await api.DELETE("/bands/{bandId}/stems/{stemId}", {
        params: { path: { bandId, stemId } },
      });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  if (idea.isPending) {
    return <p className="text-sm text-muted">Loading…</p>;
  }

  if (idea.isError || !idea.data) {
    return <p className="text-sm text-accent">That idea doesn&apos;t exist (anymore).</p>;
  }

  const detail = idea.data;
  const activeJob = detail.polishJobs.find(
    (job) => job.status === "queued" || job.status === "processing",
  );
  const lastJob = detail.polishJobs[0];

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="font-mono text-xs tracking-wider text-accent">song idea</p>
          <h2 className="font-display text-xl font-semibold tracking-tight">{detail.title}</h2>
        </div>
        <UploadButton
          label="Upload a take"
          busyLabel="Uploading"
          accept="audio/*"
          onFile={async (file, onProgress) => {
            await uploadDemoVersion(bandId, ideaId, file, onProgress);
            invalidate();
          }}
        />
      </div>

      {detail.versions.length === 0 ? (
        <p className="rounded-xl border border-dashed border-line p-5 text-sm text-muted">
          No takes yet — record in your voice memo app and upload the file here.
        </p>
      ) : (
        <div className="space-y-4">
          {detail.versions.map((version) => (
            <DemoPlayer key={version.id} bandId={bandId} version={version} />
          ))}
        </div>
      )}

      <section className="space-y-4 rounded-xl border border-line bg-surface p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h3 className="font-medium">Stems → AI demo mix</h3>
            <p className="text-sm text-muted">
              Upload labelled stems and the roadie runs the desk: gain-staged, panned, glued and
              mastered to a listenable demo. Your playing, never generated notes.
            </p>
          </div>
          <Button
            onClick={() => polish.mutate()}
            disabled={polish.isPending || !!activeJob || detail.stems.length === 0}
          >
            {activeJob ? "Mixing…" : "Mix & master stems"}
          </Button>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <ReferencePicker
            id="mix-reference"
            label="mix"
            defaultLabel="by ear · label presets"
            value={mixReference}
            onChange={setMixReference}
            references={references.data ?? []}
            versions={detail.versions}
          />
          <ReferencePicker
            id="master-reference"
            label="master"
            defaultLabel="by ear · −14 lufs"
            value={masterReference}
            onChange={setMasterReference}
            references={references.data ?? []}
            versions={detail.versions}
          />
          <UploadButton
            label="+ upload reference"
            busyLabel="Uploading"
            variant="outline"
            accept="audio/*"
            onFile={async (file, onProgress) => {
              const uploaded = await uploadReference(bandId, file, onProgress);
              invalidate();
              setMixReference(`ref:${uploaded.id}`);
              setMasterReference(`ref:${uploaded.id}`);
            }}
          />
          <p className="w-full text-xs text-faint">
            &quot;Match&quot; chases the chosen track — the mix matches its balance, stereo width and
            density; the master its eq and loudness. Mix and master can chase different tracks.
          </p>
        </div>

        {lastJob && (
          <p
            className={cn(
              "font-mono text-[11px]",
              lastJob.status === "failed" ? "text-accent" : "text-muted",
            )}
          >
            last run: {lastJob.status}
            {lastJob.error && ` — ${lastJob.error}`}
          </p>
        )}
        {polish.isError && (
          <p className="text-xs text-accent">Couldn&apos;t start — upload at least one stem first.</p>
        )}

        {detail.stems.length > 0 && (
          <ul className="space-y-2">
            {detail.stems.map((stem) => (
              <li
                key={stem.id}
                className="flex items-center justify-between rounded-lg border border-line px-3.5 py-2 text-sm"
              >
                <span className="min-w-0 truncate">
                  <span className="font-mono text-[11px] text-accent">{stem.label}</span>{" "}
                  <span className="text-muted">{stem.fileName}</span>
                </span>
                <span className="flex items-center gap-3">
                  <span className="font-mono text-[11px] text-faint">{formatBytes(stem.sizeBytes)}</span>
                  <button
                    type="button"
                    onClick={() => deleteStem.mutate(stem.id)}
                    className="text-[11px] text-faint hover:text-accent"
                  >
                    remove
                  </button>
                </span>
              </li>
            ))}
          </ul>
        )}

        <div className="flex flex-wrap items-center gap-2">
          {stemLabels.map((label) => (
            <UploadButton
              key={label}
              label={`+ ${label}`}
              busyLabel="…"
              variant="outline"
              accept="audio/*"
              onFile={async (file, onProgress) => {
                await uploadStem(bandId, ideaId, file, label, onProgress);
                invalidate();
              }}
            />
          ))}
        </div>
      </section>
    </div>
  );
}
