"use client";

import { HubConnectionBuilder } from "@microsoft/signalr";
import { useEffect } from "react";
import { getAccessToken, refreshSession } from "./auth";
import { useInvalidateBandEvents } from "./band-hooks";

const hubUrl =
  process.env.NEXT_PUBLIC_HUB_URL ?? `${process.env.NEXT_PUBLIC_API_URL ?? ""}/hubs/band`;

/**
 * One connection per open band. Realtime is cache invalidation (spec §6.2):
 * eventChanged just refetches what the page already shows.
 */
export function useBandHub(bandId: string) {
  const invalidate = useInvalidateBandEvents(bandId);

  useEffect(() => {
    let disposed = false;

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: async () => getAccessToken() ?? (await refreshSession()) ?? "",
      })
      .withAutomaticReconnect()
      .build();

    connection.on("eventChanged", invalidate);
    connection.onreconnected(() => {
      void connection.invoke("JoinBand", bandId);
      invalidate();
    });

    void connection
      .start()
      .then(() => (disposed ? undefined : connection.invoke("JoinBand", bandId)))
      .catch(() => {
        // The page works without live updates; reconnect logic retries.
      });

    return () => {
      disposed = true;
      void connection.stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- invalidate is stable per band
  }, [bandId]);
}
