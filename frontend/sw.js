const CACHE_NAME = 'smartflash-cache-v27';
const ASSETS = [
  './',
  './index.html',
  './style.css?v=23',
  './script.js?v=23',
  './vocab_data.js?v=23',
  './api.js?v=23',
  './notebook.js?v=23',
  './manifest.json',
  './icon-192.jpg',
  './icon-512.jpg',
  'https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;700;800&family=Plus+Jakarta+Sans:wght@300;400;600;700&display=swap',
  'https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css'
];

// Install Service Worker
self.addEventListener('install', (e) => {
  e.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      console.log('[Service Worker] Caching all assets');
      return cache.addAll(ASSETS);
    })
  );
  self.skipWaiting();
});

// Activate Service Worker
self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys().then((keys) => {
      return Promise.all(
        keys.map((key) => {
          if (key !== CACHE_NAME) {
            console.log('[Service Worker] Removing old cache', key);
            return caches.delete(key);
          }
        })
      );
    })
  );
  return self.clients.claim();
});

// Fetch Service Worker
self.addEventListener('fetch', (e) => {
  if (e.request.url.includes('/api/')) return; // luôn gọi mạng cho API
  e.respondWith(
    caches.match(e.request).then((cachedResponse) => {
      return cachedResponse || fetch(e.request).then((networkResponse) => {
        if (e.request.url.startsWith(self.location.origin) && e.request.method === 'GET') {
          return caches.open(CACHE_NAME).then((cache) => {
            cache.put(e.request, networkResponse.clone());
            return networkResponse;
          });
        }
        return networkResponse;
      });
    }).catch(() => {
      if (e.request.headers.get('accept').includes('text/html')) {
        return caches.match('./index.html');
      }
    })
  );
});

// Handle Notification Clicks (Focus web app & handle action buttons)
self.addEventListener('notificationclick', (e) => {
  e.notification.close();
  
  let targetUrl = './index.html';
  if (e.action === 'disable') {
    targetUrl = './index.html?action=disable-notifications';
  }
  
  e.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if ('navigate' in client && 'focus' in client) {
          client.navigate(targetUrl);
          return client.focus();
        }
      }
      if (self.clients.openWindow) {
        return self.clients.openWindow(targetUrl);
      }
    })
  );
});
