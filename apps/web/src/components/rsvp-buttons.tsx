"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { bandKeys } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";
import type { RsvpStatus } from "@/lib/types";

const options: { status: RsvpStatus; label: string }[] = [
  { status: "going", label: "I'm in" },
  { status: "maybe", label: "Maybe" },
  { status: "notGoing", label: "Can't" },
];

export function RsvpButtons({
  bandId,
  eventId,
  myStatus,
}: {
  bandId: string;
  eventId: string;
  myStatus: RsvpStatus | undefined;
}) {
  const queryClient = useQueryClient();
  const rsvp = useMutation({
    mutationFn: async (status: RsvpStatus) => {
      const { data, error } = await api.POST("/bands/{bandId}/events/{eventId}/rsvp", {
        params: { path: { bandId, eventId } },
        body: { status, note: null },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) });
    },
  });

  return (
    <div className="flex gap-2" role="group" aria-label="Rsvp">
      {options.map((option) => {
        const active = myStatus === option.status;
        return (
          <button
            key={option.status}
            type="button"
            disabled={rsvp.isPending}
            onClick={() => rsvp.mutate(option.status)}
            className={cn(
              "h-9 rounded-full border px-4 text-sm font-medium transition-colors disabled:opacity-50",
              active && option.status === "going" && "border-ok bg-ok-soft text-ok",
              active && option.status !== "going" && "border-ink bg-surface text-ink",
              !active && "border-line text-muted hover:border-ink hover:text-ink",
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
