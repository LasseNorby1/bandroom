"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { use, useState } from "react";
import { DemoPlayer } from "@/components/demo-player";
import { Recorder } from "@/components/recorder";
import { SheetDraftPanel } from "@/components/sheet-draft";
import { SongFiles } from "@/components/song-files";
import { SongSheet } from "@/components/song-sheet";
import { UploadButton } from "@/components/upload-button";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { bandKeys, useReferences, useSong } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import type { Song, SongStatus, StemLabel } from "@/lib/types";
import { formatBytes, uploadDemoVersion, uploadReference, uploadStem } from "@/lib/upload";

const stemLabels: StemLabel[] = ["drums", "bass", "guitar", "keys", "vocals", "other"];

/** Key, bpm, notes — the facts on the song, edited in place. */
function SongFacts({ bandId, song }: { bandId: string; song: Song }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState({ key: song.key ?? "", bpm: song.bpm?.toString() ?? "", notes: song.notes ?? "" });
  const save = useMutation({
    mutationFn: async () => {
      const { error } = await api.PATCH("/bands/{bandId}/songs/{songId}", {
        params: { path: { bandId, songId: song.id } },
        body: { title: null, key: draft.key, bpm: draft.bpm ? Number(draft.bpm) : null, status: null, notes: draft.notes },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      setEditing(false);
      void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });
    },
  });
  if (!editing) {
    const facts = [song.key, song.bpm != null && `${song.bpm} bpm`, song.notes].filter(Boolean).join(" · ");
    return (
      <p className="mt-1 font-mono text-[11px] text-muted">
        {facts || "no key or bpm yet"}
        {" · "}
        <button type="button" onClick={() => setEditing(true)} className="hover:text-ink">edit</button>
      </p>
    );
  }
  const field = "h-8 rounded-md border border-line bg-paper px-2 text-sm";
  return (
    <form
      className="mt-2 flex flex-wrap items-center gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate();
      }}
    >
      <input value={draft.key} onChange={(e) => setDraft({ ...draft, key: e.target.value })} placeholder="key" maxLength={12} className={cn(field, "w-20")} aria-label="Key" />
      <input value={draft.bpm} onChange={(e) => setDraft({ ...draft, bpm: e.target.value })} placeholder="bpm" type="number" min={20} max={400} className={cn(field, "w-24")} aria-label="Bpm" />
      <input value={draft.notes} onChange={(e) => setDraft({ ...draft, notes: e.target.value })} placeholder="notes (capo 2, drop d…)" maxLength={2000} className={cn(field, "min-w-48 flex-1")} aria-label="Notes" />
      <Button type="submit" size="sm" disabled={save.isPending}>Save</Button>
      <Button type="button" size="sm" variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
    </form>
  );
}

const STATUS_LABEL: Record<SongStatus, string> = { idea: "idea", working: "working", ready: "ready · repertoire", parked: "parked" };

/** Where the song is in its life — the only sorting the songs page needs; "ready" is what setlists draw from. */
function StatusPicker({ bandId, songId, status }: { bandId: string; songId: string; status: SongStatus }) {
  const queryClient = useQueryClient();
  const set = useMutation({
    mutationFn: async (next: SongStatus) => {
      const { error } = await api.PATCH("/bands/{bandId}/songs/{songId}", {
        params: { path: { bandId, songId } },
        body: { title: null, key: null, bpm: null, status: next, notes: null },
      });
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) }),
  });
  return (
    <div className="mt-2 flex flex-wrap gap-1.5" role="group" aria-label="Status">
      {(Object.keys(STATUS_LABEL) as SongStatus[]).map((s) => (
        <button
          key={s}
          type="button"
          onClick={() => s !== status && set.mutate(s)}
          aria-pressed={s === status}
          disabled={set.isPending}
          className={cn(
            "rounded-full border px-2.5 py-0.5 font-mono text-[11px] transition-colors",
            s === status ? "border-ink bg-ink text-paper" : "border-line text-muted hover:border-ink hover:text-ink",
          )}
        >
          {STATUS_LABEL[s]}
        </button>
      ))}
    </div>
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

export default function SongPage({
  params,
}: {
  params: Promise<{ bandId: string; songId: string }>;
}) {
  const { bandId, songId } = use(params);
  const queryClient = useQueryClient();
  const song = useSong(bandId, songId);
  const references = useReferences(bandId);
  const [mixReference, setMixReference] = useState("standard");
  const [masterReference, setMasterReference] = useState("standard");

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });
    void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
    void queryClient.invalidateQueries({ queryKey: ["band", bandId, "references"] });
  };

  const polish = useMutation({
    mutationFn: async () => {
      const refId = (value: string, kind: "ref" | "ver") =>
        value.startsWith(`${kind}:`) ? value.slice(4) : null;
      const { data, error } = await api.POST("/bands/{bandId}/songs/{songId}/polish", {
        params: { path: { bandId, songId } },
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

  if (song.isPending) {
    return <p className="text-sm text-muted">Loading…</p>;
  }

  if (song.isError || !song.data) {
    return <p className="text-sm text-accent">That song doesn&apos;t exist (anymore).</p>;
  }

  const detail = song.data;
  const activeJob = detail.polishJobs.find(
    (job) => job.status === "queued" || job.status === "processing",
  );
  const lastJob = detail.polishJobs[0];

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="font-mono text-xs tracking-wider text-accent">song</p>
          <h2 className="font-display text-xl font-semibold tracking-tight">{detail.song.title}</h2>
          <SongFacts bandId={bandId} song={detail.song} />
          <StatusPicker bandId={bandId} songId={songId} status={detail.song.status} />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Recorder
            onRecorded={async (file) => {
              await uploadDemoVersion(bandId, songId, file);
              invalidate();
            }}
          />
          <UploadButton
            label="Upload a take"
            busyLabel="Uploading"
            accept="audio/*"
            onFile={async (file, onProgress) => {
              await uploadDemoVersion(bandId, songId, file, onProgress);
              invalidate();
            }}
          />
        </div>
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

      <section className="rounded-xl border border-line bg-surface">
        <div className="px-5 pt-4">
          <h3 className="font-medium">Lyrics &amp; chords</h3>
          <p className="text-sm text-muted">The sheet the band plays from. Chords in brackets; transpose on screen.</p>
        </div>
        <SongSheet key={`${detail.song.id}-${detail.song.chords ?? ""}-${detail.song.durationSeconds ?? ""}`} bandId={bandId} song={detail.song} />
        <SheetDraftPanel bandId={bandId} detail={detail} />
      </section>

      <section className="rounded-xl border border-line bg-surface">
        <div className="px-5 pt-4">
          <h3 className="font-medium">Files</h3>
          <p className="text-sm text-muted">Charts, pdfs, a reference mp3 — the paper that belongs to this song.</p>
        </div>
        <SongFiles bandId={bandId} songId={songId} />
      </section>

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
                await uploadStem(bandId, songId, file, label, onProgress);
                invalidate();
              }}
            />
          ))}
        </div>
      </section>
    </div>
  );
}
