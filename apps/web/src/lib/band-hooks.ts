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
  demos: (bandId: string) => ["band", bandId, "demos"] as const,
  idea: (bandId: string, ideaId: string) => ["band", bandId, "demos", ideaId] as const,
  comments: (bandId: string, targetType: string, targetId: string) =>
    ["band", bandId, "comments", targetType, targetId] as const,
  songs: (bandId: string) => ["band", bandId, "songs"] as const,
  chat: (bandId: string) => ["band", bandId, "chat"] as const,
  messages: (bandId: string, channelId: string) => ["band", bandId, "chat", channelId] as const,
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

export function useSongIdeas(bandId: string) {
  return useQuery({
    queryKey: bandKeys.demos(bandId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/song-ideas", {
        params: { path: { bandId } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useIdea(bandId: string, ideaId: string) {
  return useQuery({
    queryKey: bandKeys.idea(bandId, ideaId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/song-ideas/{ideaId}", {
        params: { path: { bandId, ideaId } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useComments(bandId: string, targetType: "event" | "demoVersion", targetId: string) {
  return useQuery({
    queryKey: bandKeys.comments(bandId, targetType, targetId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/comments", {
        params: { path: { bandId }, query: { targetType, targetId } },
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useSongs(bandId: string) {
  return useQuery({
    queryKey: bandKeys.songs(bandId),
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/songs", { params: { path: { bandId } } });
      if (error) throw error;
      return data;
    },
  });
}

export function useChannels(bandId: string) {
  return useQuery({
    queryKey: [...bandKeys.chat(bandId), "channels"],
    queryFn: async () => {
      const { data, error } = await api.GET("/bands/{bandId}/channels", { params: { path: { bandId } } });
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
    void queryClient.invalidateQueries({ queryKey: ["band", bandId, "comments"] });
  };
}

export function useInvalidateBandDemos(bandId: string) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: bandKeys.demos(bandId) });
    void queryClient.invalidateQueries({ queryKey: ["band", bandId, "comments"] });
    void queryClient.invalidateQueries({ queryKey: bandKeys.detail(bandId) });
  };
}

export function useInvalidateBandChat(bandId: string) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: bandKeys.chat(bandId) });
  };
}
