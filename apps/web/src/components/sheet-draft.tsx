"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { ChordSheet } from "@/components/chord-sheet";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { bandKeys } from "@/lib/band-hooks";
import type { SongDetail } from "@/lib/types";

/**
 * The roadie's draft (spec §5.5): lyrics heard by whisper, chords sketched
 * from the audio, laid out as ChordPro. It sits beside the band's sheet and
 * becomes it only when someone taps "use it". Never merged, never silent.
 */
export function SheetDraftPanel({ bandId, detail }: { bandId: string; detail: SongDetail }) {
  const queryClient = useQueryClient();
  const [versionId, setVersionId] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const draft = detail.sheetDraft;
  const latest = detail.versions[0];
  const invalidate = () => void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });

  const request = useMutation({
    mutationFn: async () => {
      setError(null);
      const { error } = await api.POST("/bands/{bandId}/songs/{songId}/sheet-draft", {
        params: { path: { bandId, songId: detail.song.id } },
        body: { versionId: versionId || null },
      });
      if (error) throw new Error(Object.values((error as { errors?: Record<string, string[]> }).errors ?? {}).flat()[0] ?? "Couldn't ask the roadie.");
    },
    onSuccess: invalidate,
    onError: (e) => setError(e instanceof Error ? e.message : "Couldn't ask the roadie."),
  });

  const accept = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/bands/{bandId}/songs/{songId}/sheet-draft/accept", { params: { path: { bandId, songId: detail.song.id } } });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  const discard = useMutation({
    mutationFn: async () => {
      const { error } = await api.DELETE("/bands/{bandId}/songs/{songId}/sheet-draft", { params: { path: { bandId, songId: detail.song.id } } });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  if (!latest) return null;
  const listening = draft?.status === "queued";

  return (
    <div className="space-y-3 border-t border-line px-5 py-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-mono text-[11px] tracking-wider text-accent">roadie</span>
        <span className="text-sm text-muted">Draft lyrics and chords from a take. A sketch to correct, not a sheet to trust.</span>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        {detail.versions.length > 1 && (
          <select value={versionId} onChange={(e) => setVersionId(e.target.value)} className="h-8 rounded-md border border-line bg-paper px-2 text-sm" aria-label="Which take">
            <option value="">latest take (v{latest.number})</option>
            {detail.versions.map((v) => (
              <option key={v.id} value={v.id}>v{v.number} · {v.fileName}</option>
            ))}
          </select>
        )}
        <Button type="button" size="sm" variant="outline" disabled={request.isPending || listening} onClick={() => request.mutate()}>
          {listening ? "Listening…" : draft ? "Listen again" : "Draft from the take"}
        </Button>
        {error && <span className="text-xs text-accent">{error}</span>}
      </div>

      {draft?.status === "failed" && (
        <p className="text-xs text-accent">The roadie gave up: {draft.error ?? "unknown error"}.</p>
      )}

      {draft?.status === "done" && draft.draft && (
        <div className="space-y-3 rounded-lg border border-accent/30 bg-accent-soft/40 p-4">
          <p className="font-mono text-[11px] text-muted">
            heard from v{draft.versionNumber}{draft.language && ` · ${draft.language}`} · chords are a sketch
          </p>
          <ChordSheet text={draft.draft} />
          <div className="flex flex-wrap items-center gap-2">
            {detail.song.chords ? (
              <ConfirmDialog
                title="Replace the chord sheet?"
                description="The sheet the band wrote is replaced by the roadie's draft. You can still edit it afterwards."
                confirmLabel="Use the draft"
                onConfirm={async () => {
                  await accept.mutateAsync();
                  return null;
                }}
              >
                <Button type="button" size="sm">Use as chord sheet</Button>
              </ConfirmDialog>
            ) : (
              <Button type="button" size="sm" disabled={accept.isPending} onClick={() => accept.mutate()}>Use as chord sheet</Button>
            )}
            <Button type="button" size="sm" variant="ghost" disabled={discard.isPending} onClick={() => discard.mutate()}>Discard</Button>
          </div>
        </div>
      )}
    </div>
  );
}
