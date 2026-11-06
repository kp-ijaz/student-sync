// Student Sync service worker.
// Caches only public static assets (styles, scripts, icons, offline page). API responses, pages with user data
// and files are never cached here. Notes saved for offline reading live in the signed-in user's localStorage and
// are cleared on sign-out.
const CACHE = "ss-static-v3";
const PRECACHE = ["/offline.html", "/css/app.css", "/js/app.js", "/icons/favicon-32.png", "/images/brand/mark.png", "/icons/icon-192.png", "/manifest.webmanifest"];

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(CACHE).then((c) => c.addAll(PRECACHE)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (e) => {
  e.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener("fetch", (e) => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;
  // Navigations: always go to the network; show the offline page only when the network is unavailable.
  if (req.mode === "navigate") {
    e.respondWith(fetch(req).catch(() => caches.match("/offline.html")));
    return;
  }
  // Static assets: stale-while-revalidate for our own css/js/icons/fonts only.
  if (/\.(css|js|svg|png|woff2?)$/.test(url.pathname) && !url.pathname.startsWith("/_blazor")) {
    e.respondWith(caches.open(CACHE).then(async (cache) => {
      const cached = await cache.match(req);
      const network = fetch(req).then((res) => { if (res.ok) cache.put(req, res.clone()); return res; }).catch(() => cached);
      return cached || network;
    }));
  }
});
