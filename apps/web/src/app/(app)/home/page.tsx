"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";

export default function HomePage() {
  const queryClient = useQueryClient();

  const bands = useQuery({
    queryKey: ["bands"],
    queryFn: async () => {
      const { data, error } = await api.GET("/bands");
      if (error) throw error;
      return data;
    },
  });

  const [name, setName] = useState("");
  const createBand = useMutation({
    mutationFn: async (bandName: string) => {
      const { data, error } = await api.POST("/bands", { body: { name: bandName, timeZone: null } });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      setName("");
      void queryClient.invalidateQueries({ queryKey: ["bands"] });
    },
  });

  if (bands.isPending) {
    return <p className="text-sm text-muted">Loading your bands…</p>;
  }

  if (bands.isError) {
    return <p className="text-sm text-accent">Couldn&apos;t load your bands — is the api running?</p>;
  }

  return (
    <div className="space-y-10">
      <section className="space-y-1">
        <p className="font-mono text-xs tracking-wider text-accent">your bands</p>
        <h1 className="font-display text-2xl font-semibold tracking-tight">
          {bands.data.length === 0 ? "Start your first band" : "Pick a room"}
        </h1>
      </section>

      {bands.data.length > 0 && (
        <ul className="space-y-3">
          {bands.data.map((band) => (
            <li
              key={band.id}
              className="flex items-center justify-between rounded-xl border border-line bg-surface px-5 py-4"
            >
              <div>
                <p className="font-medium">{band.name}</p>
                <p className="text-xs text-muted">availability, finder and events land here next</p>
              </div>
              <span className="rounded-full border border-line px-2.5 py-0.5 font-mono text-[11px] text-muted">
                {band.role}
              </span>
            </li>
          ))}
        </ul>
      )}

      <form
        className="space-y-3 rounded-xl border border-line bg-surface p-5"
        onSubmit={(event) => {
          event.preventDefault();
          if (name.trim().length > 0) {
            createBand.mutate(name.trim());
          }
        }}
      >
        <p className="text-sm font-medium">Create a band</p>
        <div className="flex gap-2">
          <Input
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="Band name"
            maxLength={100}
            aria-label="Band name"
          />
          <Button type="submit" disabled={createBand.isPending || name.trim().length === 0}>
            {createBand.isPending ? "Creating…" : "Create"}
          </Button>
        </div>
        {createBand.isError && <p className="text-sm text-accent">That didn&apos;t work — try another name.</p>}
        <p className="text-xs text-faint">You become the admin; everyone else joins via an invite link.</p>
      </form>
    </div>
  );
}
