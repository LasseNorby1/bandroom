import type { IsoDay, PracticeSlot } from "./types";

const day = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short" });
const dayLong = new Intl.DateTimeFormat("en-GB", { weekday: "long", day: "numeric", month: "long" });

/** "2026-09-10" → "Thu 10 Sep" (dates are band-local; parse as plain days). */
export function formatDay(isoDate: string): string {
  return day.format(new Date(`${isoDate}T00:00:00`));
}

export function formatDayLong(isoDate: string): string {
  return dayLong.format(new Date(`${isoDate}T00:00:00`));
}

export const DAY_ORDER: IsoDay[] = [
  "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
];

export const DAY_SHORT: Record<IsoDay, string> = {
  monday: "mon",
  tuesday: "tue",
  wednesday: "wed",
  thursday: "thu",
  friday: "fri",
  saturday: "sat",
  sunday: "sun",
};

/** Mirrors the api's SlotTimes constants — display only. */
export const SLOT_LABEL: Record<PracticeSlot, string> = {
  afternoon: "afternoon · 14–17",
  evening: "evening · 19–22",
};

/** JS getDay() (0 = sunday) → IsoDay. */
export function isoDayOfDate(isoDate: string): IsoDay {
  const jsDay = new Date(`${isoDate}T00:00:00`).getDay();
  return ([
    "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
  ] as IsoDay[])[jsDay];
}

/** The Monday of the week an iso date falls in. */
export function mondayOf(isoDate: string): string {
  const offset = DAY_ORDER.indexOf(isoDayOfDate(isoDate));
  return addDays(isoDate, -offset);
}

/** Whole weeks from one Monday to another (negative when `to` is earlier). */
export function weeksBetween(fromMonday: string, toMonday: string): number {
  const ms = new Date(`${toMonday}T00:00:00`).getTime() - new Date(`${fromMonday}T00:00:00`).getTime();
  return Math.round(ms / (7 * 24 * 60 * 60 * 1000));
}

/** Today as an iso date in the browser's zone (band-local is close enough for "which week is it"). */
export function todayIso(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const dayOfMonth = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${dayOfMonth}`;
}

export function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00`);
  date.setDate(date.getDate() + days);
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const dayOfMonth = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${dayOfMonth}`;
}
