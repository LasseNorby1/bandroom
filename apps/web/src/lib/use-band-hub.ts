"use client";

import { HubConnectionBuilder } from "@microsoft/signalr";
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
        accessTokenFactory: async () => getAccessToken() ?? (await refreshSession()) ?? "",
      })
      .withAutomaticReconnect()
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

    // Deferred start: react strict mode mounts effects twice in dev, and a
    // connection stopped mid-negotiation makes signalr log an error. The
    // throwaway first mount's cleanup runs before this timer fires, so only
    // the surviving mount ever starts negotiating.
    const startTimer = setTimeout(() => {
      if (disposed) {
        return;
      }
      void connection
        .start()
        .then(() => (disposed ? connection.stop() : connection.invoke("JoinBand", bandId)))
        .catch(() => {
          // The page works without live updates; reconnect logic retries.
        });
    }, 0);

    return () => {
      disposed = true;
      clearTimeout(startTimer);
      void connection.stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- invalidate is stable per band
  }, [bandId]);
}
