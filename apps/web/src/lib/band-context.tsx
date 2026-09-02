"use client";

import { createContext, useContext } from "react";
import type { BandDetail, Member } from "./types";

export type BandContextValue = {
  bandId: string;
  detail: BandDetail | undefined;
  me: Member | undefined;
  isAdmin: boolean;
};

const BandContext = createContext<BandContextValue | null>(null);

export function BandProvider({ value, children }: { value: BandContextValue; children: React.ReactNode }) {
  return <BandContext.Provider value={value}>{children}</BandContext.Provider>;
}

export function useBandContext(): BandContextValue {
  const value = useContext(BandContext);
  if (!value) {
    throw new Error("useBandContext must be used inside /b/[bandId]");
  }
  return value;
}
