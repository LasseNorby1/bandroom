"use client";

import { HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr";
import { useEffect } from "react";
import { getAccessToken, refreshSession } from "./auth";
import {
  useInvalidateBandChat,
  useInvalidateBandDemos,
  useInvalidateBandEvents,
} from "./band-hooks";

const hubUrl =
  process.env.NEXT_PUBLIC_HUB_URL ?? `${process.env.NEXT_PUBLIC_API_URL ?? ""}/hubs/band`;

/**
 * One connection per open band. Realtime is cache invalidation (spec §6.2):
 * eventChanged just refetches what the page already shows.
 */
export function useBandHub(bandId: string) {
  const invalidate = useInvalidateBandEvents(bandId);
  const invalidateDemos = useInvalidateBandDemos(bandId);
  const invalidateChat = useInvalidateBandChat(bandId);

  useEffect(() => {
    let disposed = false;

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        // getAccessToken is expiry-aware, so a post-sleep renegotiation
        // refreshes instead of presenting a stale token (and 401ing).
        accessTokenFactory: async () => getAccessToken() ?? (await refreshSession()) ?? "",
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) =>
          Math.min(30_000, 1000 * 2 ** context.previousRetryCount),
      })
      .build();

    connection.on("eventChanged", invalidate);
    connection.on("demoChanged", invalidateDemos);
    connection.on("messageAdded", invalidateChat);
    connection.onreconnected(() => {
      void connection.invoke("JoinBand", bandId);
      invalidate();
      invalidateDemos();
      invalidateChat();
    });

    let retryTimer: ReturnType<typeof setTimeout> | undefined;

    const start = () => {
      if (disposed || connection.state !== HubConnectionState.Disconnected) {
        return;
      }
      void connection
        .start()
        .then(() => (disposed ? connection.stop() : connection.invoke("JoinBand", bandId)))
        .catch(() => {
          // The page works without live updates; keep trying quietly.
          if (!disposed) {
            retryTimer = setTimeout(start, 15_000);
          }
        });
    };

    // Automatic reconnect only covers drops after a successful start; when it
    // gives up (or the initial start never succeeded), begin again.
    connection.onclose(() => {
      if (!disposed) {
        retryTimer = setTimeout(start, 5_000);
      }
    });

    // Waking from sleep / coming back online shouldn't wait out a backoff.
    const kick = () => {
      if (document.visibilityState === "visible") {
        start();
      }
    };
    document.addEventListener("visibilitychange", kick);
    window.addEventListener("online", kick);

    // Deferred start: react strict mode mounts effects twice in dev, and a
    // connection stopped mid-negotiation makes signalr log an error. The
    // throwaway first mount's cleanup runs before this timer fires, so only
    // the surviving mount ever starts negotiating.
    const startTimer = setTimeout(start, 0);

    return () => {
      disposed = true;
      clearTimeout(startTimer);
      clearTimeout(retryTimer);
      document.removeEventListener("visibilitychange", kick);
      window.removeEventListener("online", kick);
      void connection.stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- invalidate is stable per band
  }, [bandId]);
}
