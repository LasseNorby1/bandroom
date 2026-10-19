"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys } from "@/lib/band-hooks";
import { formatBytes, uploadSongFile } from "@/lib/upload";

/** Lyrics, chords, charts — the paper that belongs to a song. */
export function SongFiles({ bandId, songId }: { bandId: string; songId: string }) {
  const { me, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const key = ["band", bandId, "songs", songId, "files"] as const;

  const files = useQuery({
    queryKey: key,
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/songs/{songId}/files", { params: { path: { bandId, songId } } });
      if (error) throw error;
      return data;
    },
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: key });
    void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
    void queryClient.invalidateQueries({ queryKey: bandKeys.songs(bandId) });
  };

  const remove = useMutation({
    mutationFn: async (fileId: string) => {
      const { error } = await api.DELETE("/bands/{bandId}/song-files/{fileId}", { params: { path: { bandId, fileId } } });
      if (error) throw error;
    },
    onSuccess: invalidate,
  });

  async function open(fileId: string) {
    const { data } = await api.GET("/bands/{bandId}/song-files/{fileId}/download", { params: { path: { bandId, fileId } } });
    if (data) window.open(data.url, "_blank", "noopener");
  }

  return (
    <div className="space-y-2 border-t border-line px-5 py-3">
      {files.data && files.data.length > 0 && (
        <ul className="space-y-1">
          {files.data.map((file) => (
            <li key={file.id} className="flex items-center gap-3 text-sm">
              <button type="button" onClick={() => void open(file.id)} className="min-w-0 truncate underline-offset-4 hover:underline">
                {file.title}
              </button>
              <span className="font-mono text-[11px] text-faint">
                {file.fileName.split(".").pop()?.toLowerCase()} · {formatBytes(file.sizeBytes)} · {file.uploaderName}
              </span>
              {(isAdmin || file.uploaderName === me?.displayName) && (
                <button type="button" onClick={() => remove.mutate(file.id)} className="ml-auto text-[11px] text-faint hover:text-accent">
                  remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      <div className="flex items-center gap-3">
        <input
          ref={inputRef}
          type="file"
          accept=".pdf,.txt,.md,.png,.jpg,.jpeg,.webp,application/pdf,text/plain,text/markdown,image/*"
          className="hidden"
          onChange={async (event) => {
            const file = event.target.files?.[0];
            event.target.value = "";
            if (!file) return;
            setError(null);
            setProgress(0);
            try {
              await uploadSongFile(bandId, songId, file, setProgress);
              invalidate();
            } catch {
              setError("Upload failed — pdf, text or image up to 20 MB.");
            } finally {
              setProgress(null);
            }
          }}
        />
        <button
          type="button"
          disabled={progress !== null}
          onClick={() => inputRef.current?.click()}
          className="font-mono text-[11px] text-muted hover:text-ink disabled:opacity-50"
        >
          {progress !== null ? `uploading ${Math.round(progress * 100)}%` : "+ lyrics, chords or a chart"}
        </button>
        {error && <span className="text-xs text-accent">{error}</span>}
      </div>
    </div>
  );
}
