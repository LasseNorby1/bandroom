"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { use } from "react";
import { BandProvider } from "@/lib/band-context";
import { useBand, useMe } from "@/lib/band-hooks";
import { useBandHub } from "@/lib/use-band-hub";
import { cn } from "@/lib/cn";

const tabs = [
  { segment: "", label: "Home" },
  { segment: "/availability", label: "Availability" },
  { segment: "/finder", label: "Finder" },
  { segment: "/settings", label: "Settings" },
];

export default function BandLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ bandId: string }>;
}) {
  const { bandId } = use(params);
  const pathname = usePathname();
  const band = useBand(bandId);
  const me = useMe();
  useBandHub(bandId);

  const myMember = band.data?.members.find((member) => member.userId === me.data?.id);
  const base = `/b/${bandId}`;

  return (
    <BandProvider
      value={{
        bandId,
        detail: band.data,
        me: myMember,
        isAdmin: myMember?.role === "admin",
      }}
    >
      <div className="space-y-6">
        <div className="space-y-3">
          <div className="flex items-baseline justify-between">
            <h1 className="font-display text-2xl font-semibold tracking-tight">
              {band.data?.band.name ?? "…"}
            </h1>
            <Link href="/home" className="text-xs text-muted hover:text-ink">
              switch band
            </Link>
          </div>
          <nav className="flex gap-1 overflow-x-auto border-b border-line pb-px">
            {tabs.map((tab) => {
              const href = `${base}${tab.segment}`;
              const active = tab.segment === "" ? pathname === base : pathname.startsWith(href);
              return (
                <Link
                  key={tab.label}
                  href={href}
                  className={cn(
                    "-mb-px whitespace-nowrap border-b-2 px-3 py-2 text-sm",
                    active
                      ? "border-accent font-medium text-ink"
                      : "border-transparent text-muted hover:text-ink",
                  )}
                >
                  {tab.label}
                </Link>
              );
            })}
          </nav>
        </div>
        {children}
      </div>
    </BandProvider>
  );
}
