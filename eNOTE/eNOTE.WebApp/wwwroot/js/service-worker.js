// eNOTE Service Worker (PWA): Offline-Grundgerüst + Cache.
const CACHE = 'enote-cache-v2-private-media';
const OFFLINE_URLS = ['/app.css', '/js/enote.js', '/manifest.webmanifest'];

self.addEventListener('install', (e) => {
    e.waitUntil(caches.open(CACHE).then((c) => c.addAll(OFFLINE_URLS)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
    e.waitUntil(
        caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim())
    );
});

// Network-first für die App (Blazor braucht Live-Verbindung), Cache als Fallback.
self.addEventListener('fetch', (e) => {
    const url = new URL(e.request.url);
    // Niemals angemeldete Dokumente, Belege oder Medien im gemeinsamen Tablet-Cache ablegen.
    if (e.request.method !== 'GET' || url.origin !== self.location.origin || !OFFLINE_URLS.includes(url.pathname) || url.search) return;
    e.respondWith(
        fetch(e.request)
            .then((resp) => {
                if (resp.ok) {
                    const clone = resp.clone();
                    caches.open(CACHE).then((c) => c.put(e.request, clone));
                }
                return resp;
            })
            .catch(() => caches.match(e.request))
    );
});
