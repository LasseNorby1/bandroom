"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export const bandKeys = {
  detail: (bandId: string) => ["band", bandId] as const,
  availability: (bandId: string) => ["band", bandId, "availability"] as const,
  myAvailability: (bandId: string) => ["band", bandId, "availability", "me"] as const,
  finder: (bandId: string) => ["band", bandId, "finder"] as const,
  events: (bandId: string) => ["band", bandId, "events"] as const,
  event: (bandId: string, eventId: string) => ["band", bandId, "events", eventId] as const,
  invites: (bandId: string) => ["band", bandId, "invites"] as const,
};

export function useMe() {
  return useQuery({
    queryKey: ["me"],
    staleTime: 5 * 60_000,
    queryFn: async () => {
      const { data, error } = await api.GET("/auth/me");
      if (error) throw error;
      return data;
    },
  });
}

export function useBand(bandId: string) {
  return useQuery({
    queryKey: bandKeys.detail(bandId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}", { params: { path: { bandId } } });
      if (error) throw error;
      return data;
    },
  });
}

export function useMyAvailability(bandId: string) {
  return useQuery({
    queryKey: bandKeys.myAvailability(bandId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/availability/me", {
        params: { path: { bandId } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useBandAvailability(bandId: string, days: number) {
  return useQuery({
    queryKey: [...bandKeys.availability(bandId), days],
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/availability", {
        params: { path: { bandId }, query: { days } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useFinder(bandId: string, days = 21) {
  return useQuery({
    queryKey: [...bandKeys.finder(bandId), days],
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/practice-finder", {
        params: { path: { bandId }, query: { days } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useEvents(bandId: string, days = 60) {
  return useQuery({
    queryKey: [...bandKeys.events(bandId), days],
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/events", {
        params: { path: { bandId }, query: { days } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useEvent(bandId: string, eventId: string) {
  return useQuery({
    queryKey: bandKeys.event(bandId, eventId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/events/{eventId}", {
        params: { path: { bandId, eventId } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useInvites(bandId: string) {
  return useQuery({
    queryKey: bandKeys.invites(bandId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/invites", { params: { path: { bandId } } });
      if (error) throw error;
      return data;
    },
  });
}

/** Everything event-shaped for a band, refetched — the SignalR handler calls this. */
export function useInvalidateBandEvents(bandId: string) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: bandKeys.events(bandId) });
    void queryClient.invalidateQueries({ queryKey: bandKeys.finder(bandId) });
  };
}
