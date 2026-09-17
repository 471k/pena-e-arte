// Hand-rolled, Cache-API-based service worker — no Workbox (a new dependency, even as a CDN
// script this app has no precedent of loading a third-party runtime script). Bump CACHE_NAME
// on any future change to this file so returning visitors pick up the new shell rather than
// serving a stale cached one indefinitely.
const CACHE_NAME = "tattooos-shell-v4";
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

  // Never intercept cross-origin requests (third-party APIs like Nominatim geocoding, R2
  // asset URLs, etc.) — this shell cache only ever has same-origin app-shell assets to serve,
  // and re-issuing a cross-origin fetch from the service worker's own execution context is
  // unnecessary and, in at least Playwright's test harness, breaks route-level mocking of that
  // request entirely (the mock never sees a request that originates from the SW, not the page).
  if (url.origin !== self.location.origin) {
    return;
  }

  // Never intercept API calls or the SignalR hub — always hit the network. This app's data is
  // real-time/multi-tenant-sensitive; a stale cached appointment list or client roster is worse
  // than a network-error state.
  if (url.pathname.startsWith("/api/") || url.pathname.startsWith("/hubs/")) {
    return;
  }

  // Cache API only supports GET; caching a POST/PUT/etc. throws "Request method is unsupported".
  // Let every non-GET request (form posts, analytics beacons, ...) go straight to the network.
  if (event.request.method !== "GET") {
    return;
  }

  // Network-first for the HTML shell itself (page navigations + manifest.json) — the shell's
  // own content changes on every deploy (its <script> tags point at the CURRENT build's
  // content-hashed JS/CSS filenames), so caching it cache-first pins a returning visitor to
  // whichever build they first loaded, indefinitely, with no way to pick up a new deploy short
  // of manually clearing the service worker's cache — a real bug that hid multiple real fixes
  // from a real user's browser across several deploys before being caught (2026-09-17). Falls
  // back to the cached shell only when the network is genuinely unavailable (offline), which is
  // this cache's actual purpose per SHELL_ASSETS being pre-populated on install.
  const isShellRequest = event.request.mode === "navigate" || url.pathname === "/manifest.json";
  if (isShellRequest) {
    event.respondWith(
      fetch(event.request)
        .then((response) => {
          if (response.ok) {
            const clone = response.clone();
            caches.open(CACHE_NAME).then((cache) => cache.put(event.request, clone));
          }
          return response;
        })
        .catch(() => caches.match(event.request))
    );
    return;
  }

  // Cache-first for everything else — Vite's built JS/CSS/asset filenames are content-hashed,
  // so a cached entry can never go stale: the same URL always means the same bytes, and a new
  // deploy always means a new URL (referenced by the freshly network-fetched shell above).
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
