import { cn } from "@/lib/cn";
import type { EventStatus } from "@/lib/types";

const styles: Record<EventStatus, string> = {
  proposed: "border-line text-muted",
  confirmed: "border-ok/40 bg-ok-soft text-ok",
  cancelled: "border-line text-faint line-through",
};

export function StatusChip({ status, className }: { status: EventStatus; className?: string }) {
  return (
    <span
      className={cn(
        "rounded-full border px-2.5 py-0.5 font-mono text-[11px]",
        styles[status],
        className,
      )}
    >
      {status}
    </span>
  );
}
