import { cn } from "@/lib/cn";

export function Wordmark({ className }: { className?: string }) {
  return (
    <span className={cn("inline-flex items-baseline gap-2", className)}>
      <span className="font-display text-xl font-bold tracking-tight">Bandroom</span>
      <span className="size-2 shrink-0 self-center rounded-full bg-accent" aria-hidden />
    </span>
  );
}
