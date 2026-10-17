/**
 * "Add to home screen", the pre-Expo way (spec §6.2). Android browsers hand us
 * a deferred install prompt; iOS has no api, only the Share-sheet path. The
 * prompt event fires early — often before any page component mounts — so it is
 * captured at module load and handed out later.
 */

type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
};

let deferredPrompt: BeforeInstallPromptEvent | null = null;
const listeners = new Set<() => void>();

if (typeof window !== "undefined") {
  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferredPrompt = event as BeforeInstallPromptEvent;
    listeners.forEach((fn) => fn());
  });
  window.addEventListener("appinstalled", () => {
    deferredPrompt = null;
    listeners.forEach((fn) => fn());
  });
}

export function onInstallChange(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function isStandalone(): boolean {
  if (typeof window === "undefined") return true;
  return (
    window.matchMedia("(display-mode: standalone)").matches ||
    ("standalone" in navigator && (navigator as { standalone?: boolean }).standalone === true)
  );
}

export function isIos(): boolean {
  if (typeof navigator === "undefined") return false;
  return /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

export function isPhone(): boolean {
  if (typeof navigator === "undefined") return false;
  return isIos() || /Android/.test(navigator.userAgent);
}

export function canPromptInstall(): boolean {
  return deferredPrompt !== null;
}

/** Shows the browser's install dialog; true when the person accepted. */
export async function promptInstall(): Promise<boolean> {
  const event = deferredPrompt;
  if (!event) return false;
  await event.prompt();
  const choice = await event.userChoice;
  if (choice.outcome === "accepted") deferredPrompt = null;
  return choice.outcome === "accepted";
}

const DISMISS_KEY = "bandroom.install.dismissed";

export function installDismissed(): boolean {
  try {
    return localStorage.getItem(DISMISS_KEY) === "1";
  } catch {
    return false;
  }
}

export function dismissInstall(): void {
  try {
    localStorage.setItem(DISMISS_KEY, "1");
  } catch {
    // Private mode or blocked storage: the banner just comes back next time.
  }
}
