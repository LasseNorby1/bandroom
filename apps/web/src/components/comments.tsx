"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useComments } from "@/lib/band-hooks";
import { formatSeconds } from "@/lib/upload";

export function Comments({
  bandId,
  targetType,
  targetId,
  getCurrentTime,
  onSeek,
}: {
  bandId: string;
  targetType: "event" | "demoVersion";
  targetId: string;
  /** When set (demo players), the composer can pin the comment at playback time. */
  getCurrentTime?: () => number | null;
  onSeek?: (seconds: number) => void;
}) {
  const { me, isAdmin } = useBandContext();
  const queryClient = useQueryClient();
  const comments = useComments(bandId, targetType, targetId);
  const [body, setBody] = useState("");
  const [pinTime, setPinTime] = useState(true);

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: bandKeys.comments(bandId, targetType, targetId) });

  const add = useMutation({
    mutationFn: async () => {
      const atSeconds =
        targetType === "demoVersion" && pinTime ? (getCurrentTime?.() ?? undefined) : undefined;
      const { data, error } = await api.POST("/bands/{bandId}/comments", {
        params: { path: { bandId } },
        body: { targetType, targetId, body, atSeconds },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setBody("");
      void invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: async (commentId: string) => {
      const { error } = await api.DELETE("/bands/{bandId}/comments/{commentId}", {
        params: { path: { bandId, commentId } },
      });
      if (error) throw error;
    },
    onSuccess: () => void invalidate(),
  });

  return (
    <div className="space-y-3">
      {comments.data && comments.data.length > 0 && (
        <ul className="space-y-2">
          {comments.data.map((comment) => (
            <li key={comment.id} className="flex items-baseline gap-2 text-sm">
              {comment.atSeconds != null && (
                <button
                  type="button"
                  onClick={() => onSeek?.(comment.atSeconds!)}
                  className="font-mono text-[11px] text-accent hover:underline"
                >
                  {formatSeconds(comment.atSeconds)}
                </button>
              )}
              <span className="font-medium">{comment.authorName}</span>
              <span className="min-w-0 flex-1 text-muted">{comment.body}</span>
              {(isAdmin || comment.authorMembershipId === me?.membershipId) && (
                <button
                  type="button"
                  onClick={() => remove.mutate(comment.id)}
                  className="text-[11px] text-faint hover:text-accent"
                  aria-label="Delete comment"
                >
                  ✕
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      <form
        className="flex items-center gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (body.trim()) add.mutate();
        }}
      >
        <Input
          value={body}
          onChange={(event) => setBody(event.target.value)}
          placeholder={targetType === "demoVersion" ? "Comment — pins at playback time" : "Comment"}
          maxLength={1000}
          className="h-9"
        />
        {targetType === "demoVersion" && getCurrentTime && (
          <label className="flex shrink-0 items-center gap-1 font-mono text-[11px] text-muted">
            <input
              type="checkbox"
              checked={pinTime}
              onChange={(event) => setPinTime(event.target.checked)}
            />
            pin time
          </label>
        )}
        <Button type="submit" size="sm" variant="outline" disabled={add.isPending || !body.trim()}>
          Send
        </Button>
      </form>
    </div>
  );
}
