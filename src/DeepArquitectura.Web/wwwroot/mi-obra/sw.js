// Service worker de la app del cliente (/mi-obra/). Red primero para las pantallas (siempre lo último),
// y si no hay señal muestra la última versión vista. Las fotos no cambian: se guardan y se sirven de la caché.
const VERSION = 'mi-obra-v1';
const PANTALLAS = VERSION + '-pantallas';
const MEDIOS = VERSION + '-medios';
const BASE = ['/mi-obra/sin-conexion.html', '/css/cliente.css', '/img/logo.png', '/img/app/icon-192.png'];

self.addEventListener('install', e => {
  e.waitUntil(caches.open(PANTALLAS).then(c => c.addAll(BASE)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', e => {
  e.waitUntil(caches.keys()
    .then(ks => Promise.all(ks.filter(k => !k.startsWith(VERSION)).map(k => caches.delete(k))))
    .then(() => self.clients.claim()));
});

self.addEventListener('message', e => {
  // Al cerrar sesión no queda nada de la obra guardado en el teléfono.
  if (e.data === 'salir') e.waitUntil(caches.keys().then(ks => Promise.all(ks.map(k => caches.delete(k)))));
});

self.addEventListener('fetch', e => {
  const req = e.request;
  const url = new URL(req.url);
  if (req.method !== 'GET' || url.origin !== location.origin) return;

  if (req.mode === 'navigate') {
    e.respondWith(fetch(req).then(res => {
      if (res.ok && !res.redirected) {
        const copia = res.clone();
        caches.open(PANTALLAS).then(c => c.put(req, copia));
      }
      return res;
    }).catch(() => caches.match(req).then(r => r || caches.match('/mi-obra/sin-conexion.html'))));
    return;
  }

  if (url.pathname.startsWith('/mi-obra/medios/')) {
    // Los videos se piden por rangos: esos van directo a la red.
    if (req.headers.has('range')) return;
    e.respondWith(caches.open(MEDIOS).then(c => c.match(req).then(r => r || fetch(req).then(res => {
      if (res.ok && res.status === 200 && (res.headers.get('content-type') || '').startsWith('image/')) c.put(req, res.clone());
      return res;
    }))));
    return;
  }

  if (url.pathname.startsWith('/css/') || url.pathname.startsWith('/img/')) {
    e.respondWith(caches.match(req).then(r => {
      const red = fetch(req).then(res => {
        if (res.ok) caches.open(PANTALLAS).then(c => c.put(req, res.clone()));
        return res;
      }).catch(() => r);
      return r || red;
    }));
  }
});