import { api } from "./api";

/// Web push for the installed pwa (spec principle 3). The service worker only
/// handles notifications; registration happens from the account page.

export type PushState = "unsupported" | "denied" | "off" | "on" | "needs-install";

function isIosNotInstalled(): boolean {
  const ua = navigator.userAgent;
  const ios = /iPhone|iPad|iPod/.test(ua) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const standalone = window.matchMedia("(display-mode: standalone)").matches || ("standalone" in navigator && (navigator as { standalone?: boolean }).standalone === true);
  return ios && !standalone;
}

export function pushSupport(): PushState {
  if (typeof window === "undefined") return "unsupported";
  if (!("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window)) {
    return isIosNotInstalled() ? "needs-install" : "unsupported";
  }
  if (Notification.permission === "denied") return "denied";
  return "off";
}

// The worker posts one-tap answers to the api, and it's killed whenever idle,
// so the api base rides along in its url rather than in a message.
const swUrl = `/sw.js?api=${encodeURIComponent(process.env.NEXT_PUBLIC_API_URL ?? "/api")}`;

async function registration(): Promise<ServiceWorkerRegistration> {
  const reg = await navigator.serviceWorker.register(swUrl);
  await navigator.serviceWorker.ready;
  return reg;
}

function toKey(base64url: string): Uint8Array {
  const padded = base64url + "=".repeat((4 - (base64url.length % 4)) % 4);
  const raw = atob(padded.replace(/-/g, "+").replace(/_/g, "/"));
  return Uint8Array.from(raw, (c) => c.charCodeAt(0));
}

/** Is this browser currently subscribed (and does the server know)? */
export async function currentState(): Promise<PushState> {
  const support = pushSupport();
  if (support !== "off") return support;
  try {
    const reg = await navigator.serviceWorker.getRegistration("/");
    const sub = await reg?.pushManager.getSubscription();
    return sub ? "on" : "off";
  } catch {
    return "off";
  }
}

export async function enablePush(): Promise<PushState> {
  const { data: keys } = await api.GET("/push/keys");
  if (!keys?.enabled || !keys.publicKey) throw new Error("Push isn't switched on for this server.");
  const permission = await Notification.requestPermission();
  if (permission !== "granted") return permission === "denied" ? "denied" : "off";
  const reg = await registration();
  const sub =
    (await reg.pushManager.getSubscription()) ??
    (await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: toKey(keys.publicKey) as BufferSource }));
  const json = sub.toJSON();
  const { error } = await api.POST("/push/subscriptions", {
    body: { endpoint: sub.endpoint, keys: { p256dh: json.keys?.p256dh ?? "", auth: json.keys?.auth ?? "" }, userAgent: navigator.userAgent },
  });
  if (error) throw error;
  return "on";
}

export async function disablePush(): Promise<PushState> {
  const reg = await navigator.serviceWorker.getRegistration("/");
  const sub = await reg?.pushManager.getSubscription();
  if (sub) {
    await api.DELETE("/push/subscriptions", { body: { endpoint: sub.endpoint } });
    await sub.unsubscribe();
  }
  return "off";
}
