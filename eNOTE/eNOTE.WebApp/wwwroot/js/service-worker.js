// eNOTE Service Worker (PWA): Offline-Grundgerüst + Cache.
const CACHE = 'enote-cache-v1';
const OFFLINE_URLS = ['/', '/app.css', '/js/enote.js', '/manifest.webmanifest'];

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
    if (e.request.method !== 'GET') return;
    e.respondWith(
        fetch(e.request)
            .then((resp) => {
                if (resp.ok && (e.request.destination === 'style' || e.request.destination === 'script' || e.request.destination === 'document')) {
                    const clone = resp.clone();
                    caches.open(CACHE).then((c) => c.put(e.request, clone));
                }
                return resp;
            })
            .catch(() => caches.match(e.request))
    );
});
