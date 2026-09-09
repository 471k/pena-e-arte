// Hand-rolled, Cache-API-based service worker — no Workbox (a new dependency, even as a CDN
// script this app has no precedent of loading a third-party runtime script). Bump CACHE_NAME
// on any future change to this file so returning visitors pick up the new shell rather than
// serving a stale cached one indefinitely.
const CACHE_NAME = "tattooos-shell-v1";
const SHELL_ASSETS = ["/", "/manifest.json"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(SHELL_ASSETS))
  );
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k)))
    )
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);

  // Never intercept API calls or the SignalR hub — always hit the network. This app's data is
  // real-time/multi-tenant-sensitive; a stale cached appointment list or client roster is worse
  // than a network-error state.
  if (url.pathname.startsWith("/api/") || url.pathname.startsWith("/hubs/")) {
    return;
  }

  // Cache-first for everything else (the built app shell + static assets), falling back to
  // network and caching the result for next time.
  event.respondWith(
    caches.match(event.request).then((cached) => {
      if (cached) return cached;
      return fetch(event.request).then((response) => {
        if (response.ok) {
          const clone = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put(event.request, clone));
        }
        return response;
      });
    })
  );
});
