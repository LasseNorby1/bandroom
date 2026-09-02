"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { Comments } from "@/components/comments";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import type { DemoVersion } from "@/lib/types";
import { formatBytes, formatSeconds } from "@/lib/upload";

const BAR_COUNT = 96;

function usePeaks(url: string | null): number[] | null {
  const [peaks, setPeaks] = useState<number[] | null>(null);

  useEffect(() => {
    if (!url) return;
    let cancelled = false;

    void (async () => {
      try {
        const response = await fetch(url);
        const buffer = await response.arrayBuffer();
        const audioContext = new AudioContext();
        const decoded = await audioContext.decodeAudioData(buffer);
        void audioContext.close();
        const channel = decoded.getChannelData(0);
        const blockSize = Math.max(1, Math.floor(channel.length / BAR_COUNT));
        const computed = Array.from({ length: BAR_COUNT }, (_, i) => {
          let sum = 0;
          const start = i * blockSize;
          for (let j = start; j < start + blockSize && j < channel.length; j += 32) {
            sum = Math.max(sum, Math.abs(channel[j]));
          }
          return sum;
        });
        const max = Math.max(...computed, 0.01);
        if (!cancelled) setPeaks(computed.map((p) => p / max));
      } catch {
        // Waveform is decoration; playback works without it.
        if (!cancelled) setPeaks(null);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [url]);

  return peaks;
}

export function DemoPlayer({ bandId, version }: { bandId: string; version: DemoVersion }) {
  const { me, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const audioRef = useRef<HTMLAudioElement>(null);
  const [url, setUrl] = useState<string | null>(null);
  const [playing, setPlaying] = useState(false);
  const [position, setPosition] = useState(0);
  const peaks = usePeaks(url);
  const duration = version.durationSeconds ?? audioRef.current?.duration ?? 0;

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const { data } = await api.GET("/bands/{bandId}/versions/{versionId}/stream", {
        params: { path: { bandId, versionId: version.id } },
      });
      if (!cancelled && data) setUrl(data.url);
    })();
    return () => {
      cancelled = true;
    };
  }, [bandId, version.id]);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;
    const onTime = () => setPosition(audio.currentTime);
    const onEnd = () => setPlaying(false);
    audio.addEventListener("timeupdate", onTime);
    audio.addEventListener("ended", onEnd);
    return () => {
      audio.removeEventListener("timeupdate", onTime);
      audio.removeEventListener("ended", onEnd);
    };
  }, [url]);

  const toggle = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;
    if (playing) {
      audio.pause();
      setPlaying(false);
    } else {
      void audio.play();
      setPlaying(true);
    }
  }, [playing]);

  const seek = useCallback((seconds: number) => {
    const audio = audioRef.current;
    if (!audio) return;
    audio.currentTime = seconds;
    setPosition(seconds);
    if (audio.paused) {
      void audio.play();
      setPlaying(true);
    }
  }, []);

  const remove = useMutation({
    mutationFn: async () => {
      const { error } = await api.DELETE("/bands/{bandId}/versions/{versionId}", {
        params: { path: { bandId, versionId: version.id } },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: bandKeys.demos(bandId) });
      void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
    },
  });

  const progress = duration > 0 ? position / duration : 0;

  return (
    <div className="space-y-3 rounded-xl border border-line bg-surface p-4">
      <div className="flex items-baseline gap-2">
        <span className="font-mono text-[11px] text-accent">v{version.number}</span>
        <span className="min-w-0 flex-1 truncate text-sm font-medium">
          {version.kind === "aiMix" ? "AI demo mix" : version.fileName}
        </span>
        {version.kind === "aiMix" && (
          <span className="rounded-full border border-accent/40 bg-accent-soft px-2 py-0.5 font-mono text-[10px] text-accent">
            ai mix
          </span>
        )}
        <span className="font-mono text-[11px] text-faint">
          {version.uploaderName} · {formatBytes(version.sizeBytes)}
          {version.durationSeconds != null && ` · ${formatSeconds(version.durationSeconds)}`}
        </span>
        {(isAdmin || me?.displayName === version.uploaderName) && (
          <button
            type="button"
            onClick={() => remove.mutate()}
            className="text-[11px] text-faint hover:text-accent"
          >
            delete
          </button>
        )}
      </div>

      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={toggle}
          disabled={!url}
          aria-label={playing ? "Pause" : "Play"}
          className="flex size-9 shrink-0 items-center justify-center rounded-full bg-ink text-paper disabled:opacity-40"
        >
          {playing ? "❚❚" : "▶"}
        </button>
        <button
          type="button"
          aria-label="Seek"
          className="flex h-11 flex-1 items-end gap-px"
          onClick={(event) => {
            const rect = event.currentTarget.getBoundingClientRect();
            const fraction = (event.clientX - rect.left) / rect.width;
            if (duration > 0) seek(fraction * duration);
          }}
        >
          {(peaks ?? Array.from({ length: BAR_COUNT }, () => 0.5)).map((peak, index) => (
            <span
              key={index}
              style={{ height: `${Math.max(8, peak * 100)}%` }}
              className={cn(
                "min-w-0 flex-1 rounded-sm",
                index / BAR_COUNT <= progress ? "bg-ink" : "bg-line",
              )}
            />
          ))}
        </button>
        <span className="w-12 shrink-0 text-right font-mono text-[11px] text-muted">
          {formatSeconds(position)}
        </span>
      </div>

      {url && <audio ref={audioRef} src={url} preload="metadata" />}

      <Comments
        bandId={bandId}
        targetType="demoVersion"
        targetId={version.id}
        getCurrentTime={() => (audioRef.current ? audioRef.current.currentTime : null)}
        onSeek={seek}
      />
    </div>
  );
}
