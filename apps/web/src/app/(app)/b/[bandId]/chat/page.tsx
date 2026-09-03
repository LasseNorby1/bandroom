"use client";

import { useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Fragment, use, useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { useBandContext } from "@/lib/band-context";
import { bandKeys, useChannels } from "@/lib/band-hooks";
import { cn } from "@/lib/cn";

function dayLabel(sent: Date): string {
  const now = new Date();
  const startOf = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const daysAgo = Math.round((startOf(now) - startOf(sent)) / 86_400_000);
  if (daysAgo === 0) return "today";
  if (daysAgo === 1) return "yesterday";
  return sent
    .toLocaleDateString("en-GB", {
      weekday: "long",
      day: "numeric",
      month: "long",
      ...(sent.getFullYear() !== now.getFullYear() && { year: "numeric" as const }),
    })
    .toLowerCase();
}

function useMessages(bandId: string, channelId: string | null) {
  return useInfiniteQuery({
    queryKey: channelId ? bandKeys.messages(bandId, channelId) : ["band", bandId, "chat", "none"],
    enabled: !!channelId,
    initialPageParam: null as string | null,
    queryFn: async ({ pageParam }) => {
      const { data, error } = await api.GET("/bands/{bandId}/channels/{channelId}/messages", {
        params: {
          path: { bandId, channelId: channelId! },
          query: pageParam ? { before: pageParam } : {},
        },
      });
      if (error || !data) throw error ?? new Error("failed");
      return data;
    },
    getNextPageParam: (lastPage) => lastPage.nextBefore ?? undefined,
  });
}

export default function ChatPage({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = use(params);
  const { me } = useBandContext();
  const queryClient = useQueryClient();
  const channels = useChannels(bandId);
  const [channelId, setChannelId] = useState<string | null>(null);
  const [body, setBody] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);

  const activeChannel = channelId ?? channels.data?.[0]?.id ?? null;
  const messages = useMessages(bandId, activeChannel);

  // Pages arrive newest-first; flatten oldest-first for display.
  const items = (messages.data?.pages ?? [])
    .slice()
    .reverse()
    .flatMap((page) => page.items);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [items.length]);

  const send = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/bands/{bandId}/channels/{channelId}/messages", {
        params: { path: { bandId, channelId: activeChannel! } },
        body: { body: body.trim() },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setBody("");
      void queryClient.invalidateQueries({ queryKey: bandKeys.chat(bandId) });
    },
  });

  if (channels.isPending) {
    return <p className="text-sm text-muted">Loading chat…</p>;
  }

  if (channels.isError || !channels.data) {
    return <p className="text-sm text-accent">Couldn&apos;t load chat.</p>;
  }

  return (
    <div className="flex h-[calc(100dvh-14rem)] min-h-96 flex-col gap-3">
      <div className="flex flex-wrap gap-1.5">
        {channels.data.map((channel) => (
          <button
            key={channel.id}
            type="button"
            onClick={() => setChannelId(channel.id)}
            className={cn(
              "rounded-full border px-3 py-1 font-mono text-[11px]",
              channel.id === activeChannel
                ? "border-ink bg-ink text-paper"
                : "border-line text-muted hover:border-ink hover:text-ink",
            )}
          >
            #{channel.name}
          </button>
        ))}
      </div>

      <div className="flex-1 space-y-3 overflow-y-auto rounded-xl border border-line bg-surface p-4">
        {messages.hasNextPage && (
          <button
            type="button"
            onClick={() => void messages.fetchNextPage()}
            className="mx-auto block font-mono text-[11px] text-muted hover:text-ink"
          >
            load older
          </button>
        )}
        {items.length === 0 && (
          <p className="text-sm text-muted">
            Quiet in here. Decisions about songs and gigs are findable when they happen where the
            work lives.
          </p>
        )}
        {items.map((message, index) => {
          const mine = message.authorMembershipId === me?.membershipId;
          const sent = new Date(message.createdAt);
          const newDay =
            index === 0 ||
            new Date(items[index - 1].createdAt).toDateString() !== sent.toDateString();
          return (
            <Fragment key={message.id}>
              {newDay && (
                <div className="flex items-center gap-3 py-1">
                  <span className="h-px flex-1 bg-line" />
                  <span className="font-mono text-[10px] tracking-wider text-faint">
                    {dayLabel(sent)}
                  </span>
                  <span className="h-px flex-1 bg-line" />
                </div>
              )}
              <div className={cn("max-w-[85%]", mine && "ml-auto text-right")}>
                <p className="font-mono text-[10px] text-faint">
                  {message.authorName} ·{" "}
                  {sent.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" })}
                </p>
                <p
                  className={cn(
                    "inline-block rounded-xl px-3.5 py-2 text-sm",
                    mine ? "bg-ink text-paper" : "bg-paper",
                  )}
                >
                  {message.body}
                </p>
              </div>
            </Fragment>
          );
        })}
        <div ref={bottomRef} />
      </div>

      <form
        className="flex gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (body.trim() && activeChannel) send.mutate();
        }}
      >
        <Input
          value={body}
          onChange={(event) => setBody(event.target.value)}
          placeholder="Message the band"
          maxLength={2000}
        />
        <Button type="submit" disabled={send.isPending || !body.trim()}>
          Send
        </Button>
      </form>
    </div>
  );
}
