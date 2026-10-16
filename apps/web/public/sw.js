// Bandroom service worker: web push only. No caching, no offline — the app
// stays live-from-the-api; this file exists so notifications can arrive and
// their buttons can answer without opening the app.
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

self.addEventListener("push", (event) => {
  let payload = { title: "Bandroom", body: "", url: "/home", tag: undefined, actions: [] };
  try {
    payload = { ...payload, ...event.data.json() };
  } catch {
    payload.body = event.data ? event.data.text() : "";
  }
  const actions = (payload.actions || []).map((a) => ({ action: a.action, title: a.title }));
  event.waitUntil(
    self.registration.showNotification(payload.title, {
      body: payload.body,
      tag: payload.tag,
      icon: "/icon.svg",
      badge: "/icon.svg",
      data: { url: payload.url, actions: payload.actions || [] },
      actions,
      renotify: !!payload.tag,
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  const data = event.notification.data || {};
  event.notification.close();
  const apiBase = new URL(self.location.href).searchParams.get("api") || "";
  const action = (data.actions || []).find((a) => a.action === event.action);
  if (action) {
    // One-tap answer: the url carries a signed token, so no session is needed.
    event.waitUntil(
      fetch(apiBase + action.url, { method: "POST" })
        .then((r) => self.registration.showNotification(r.ok ? "Got it" : "That answer expired", {
          body: r.ok ? `Marked “${action.title}”.` : "Open the event to answer.",
          tag: event.notification.tag,
          icon: "/icon.svg",
          data: { url: data.url },
        }))
        .catch(() => undefined),
    );
    return;
  }
  const target = new URL(data.url || "/home", self.location.origin).href;
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((windows) => {
      const open = windows.find((w) => w.url.startsWith(self.location.origin));
      if (open) {
        return open.focus().then(() => open.navigate(target));
      }
      return self.clients.openWindow(target);
    }),
  );
});
