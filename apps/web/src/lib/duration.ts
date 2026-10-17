/** "3:35" ↔ 215. Songs are entered as mm:ss; nothing here needs hours. */
export function parseDuration(text: string): number | null {
  const t = text.trim();
  if (!t) return null;
  const m = /^(\d{1,3})(?::([0-5]?\d))?$/.exec(t);
  if (!m) return null;
  return Number(m[1]) * 60 + Number(m[2] ?? 0);
}

export function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null) return "";
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${String(s).padStart(2, "0")}`;
}

/** "about 45 min" for a setlist total. */
export function formatMinutes(seconds: number): string {
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h} h` : `${h} h ${m} min`;
}
