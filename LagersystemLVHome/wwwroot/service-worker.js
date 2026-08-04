// Service worker for the Progressive Web App (PWA)
// Version: 3.0.0 - Smart cache with cache busting

const CACHE_VERSION = '3.0.0';
const CACHE_NAME = `lagersystem-v${CACHE_VERSION}`;
const RUNTIME_CACHE = `lagersystem-runtime-v${CACHE_VERSION}`;
const IMAGE_CACHE = `lagersystem-images-v${CACHE_VERSION}`;

// Resources for pre-caching (critical assets only)
const PRECACHE_URLS = [
    '/',
  '/manifest.json',
    '/offline.html'
];

// URLs that must NOT be cached
const EXCLUDED_URLS = [
    '/api/',
    '/_blazor/',
    '/signalr/',
    '/_framework/blazor.server.js'
];

// Install Event
self.addEventListener('install', (event) => {
    console.log(`[ServiceWorker] Install v${CACHE_VERSION}`);
    
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => {
  return cache.addAll(PRECACHE_URLS);
       })
.catch((error) => {
                console.error('[ServiceWorker] Precaching failed:', error);
            })
    );
    
    self.skipWaiting();
});

// Activate event - delete old cache versions
self.addEventListener('activate', (event) => {
    console.log(`[ServiceWorker] Activate v${CACHE_VERSION}`);
    
    event.waitUntil(
        caches.keys().then((cacheNames) => {
            return Promise.all(
 cacheNames
            .filter(cacheName => cacheName.startsWith('lagersystem-'))
      .filter(cacheName => 
          cacheName !== CACHE_NAME &&
    cacheName !== RUNTIME_CACHE &&
                cacheName !== IMAGE_CACHE
 )
        .map((cacheName) => {
        return caches.delete(cacheName);
        })
 );
      })
    );
    
    return self.clients.claim();
});

// Fetch Event - Intelligente Caching-Strategie
self.addEventListener('fetch', (event) => {
    const { request } = event;
    const url = new URL(request.url);

    // Ignore non-HTTP(S) schemes (chrome-extension://, etc.)
    if (!url.protocol.startsWith('http')) {
        return;
    }

    // Ignore non-GET requests
    if (request.method !== 'GET') {
 return;
   }

    // Ignoriere ausgeschlossene URLs
    if (EXCLUDED_URLS.some(excluded => url.pathname.startsWith(excluded))) {
        return;
    }
    
    // Blazor Server SignalR - immer vom Netzwerk
    if (url.pathname.startsWith('/_blazor/') || 
        url.pathname.startsWith('/_framework/blazor.server.js')) {
    event.respondWith(fetch(request));
    return;
    }
    
    // API Calls - Network Only
    if (url.pathname.startsWith('/api/')) {
        event.respondWith(networkOnly(request));
        return;
    }
    
    // CSS/JS - Network First mit Cache Fallback (Cache-Busting via URL-Parameter)
    if (url.pathname.endsWith('.css') || 
     (url.pathname.endsWith('.js') && !url.pathname.includes('_framework'))) {
        event.respondWith(networkFirstWithCacheBusting(request));
    return;
    }
    
  // Bilder - Cache First
    if (request.destination === 'image' || url.pathname.match(/\.(png|jpg|jpeg|gif|svg|webp|ico)$/)) {
  event.respondWith(cacheFirst(request, IMAGE_CACHE));
        return;
    }
    
    // Fonts - Cache First
    if (url.pathname.match(/\.(woff2?|ttf|eot)$/)) {
        event.respondWith(cacheFirst(request, CACHE_NAME));
        return;
    }
    
    // HTML/Navigation - Network First mit Offline Fallback
    if (request.mode === 'navigate' || request.headers.get('accept')?.includes('text/html')) {
        event.respondWith(networkFirstWithOffline(request));
   return;
    }
    
    // Default: Network First
    event.respondWith(networkFirst(request));
});

// Network-first with cache busting (for CSS/JS)
async function networkFirstWithCacheBusting(request) {
    const cache = await caches.open(RUNTIME_CACHE);
    const url = new URL(request.url);

    try {
        // Check whether a newer cache entry is available via ETag / Last-Modified
        const cachedResponse = await cache.match(request);
    
        // Fetch the new version from the server
 const networkResponse = await fetch(request, {
            cache: 'reload' // Umgehe Browser-Cache
        });
  
        // Check whether anything changed
        if (networkResponse && networkResponse.status === 200) {
            const newETag = networkResponse.headers.get('ETag');
    const cachedETag = cachedResponse?.headers.get('ETag');
            
         // Only cache if something changed or nothing is cached yet
            if (!cachedResponse || newETag !== cachedETag) {
     cache.put(request, networkResponse.clone());
            }
        }
        
        return networkResponse;
    } catch (error) {
console.log('[ServiceWorker] Network failed, serving from cache:', request.url);
        
        // Fallback auf Cache
        const cachedResponse = await cache.match(request);
        if (cachedResponse) {
            return cachedResponse;
        }
        
        throw error;
    }
}

// Network only (no caching)
async function networkOnly(request) {
    return fetch(request);
}

// Network First (mit Cache Fallback)
async function networkFirst(request) {
    const cache = await caches.open(RUNTIME_CACHE);
    
    try {
        const response = await fetch(request);
 
        if (response && response.status === 200) {
            cache.put(request, response.clone());
        }
    
        return response;
    } catch (error) {
     console.log('[ServiceWorker] Network failed, trying cache:', request.url);

        const cached = await cache.match(request);
  if (cached) {
    return cached;
  }
        
    throw error;
  }
}

// Cache First (mit Network Fallback)
async function cacheFirst(request, cacheName = CACHE_NAME) {
 const cache = await caches.open(cacheName);
    const cached = await cache.match(request);
    
    if (cached) {
     // Background update for long-cached resources
    fetch(request).then((response) => {
          if (response && response.status === 200) {
   cache.put(request, response.clone());
          }
        }).catch(() => {});
        
     return cached;
    }
    
    try {
    const response = await fetch(request);
        
  if (response && response.status === 200) {
      cache.put(request, response.clone());
        }
    
     return response;
    } catch (error) {
        console.error('[ServiceWorker] Fetch failed:', error);
        throw error;
    }
}

// Network First mit Offline Fallback
async function networkFirstWithOffline(request) {
    try {
        const response = await fetch(request);
     
        if (response && response.status === 200) {
          const cache = await caches.open(RUNTIME_CACHE);
         cache.put(request, response.clone());
        }
    
        return response;
    } catch (error) {
    console.log('[ServiceWorker] Network failed, trying cache:', request.url);
   
const cached = await caches.match(request);
if (cached) {
 return cached;
        }
      
    console.log('[ServiceWorker] Showing offline page');
      return caches.match('/offline.html');
    }
}

// Message Handler
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
    
    if (event.data && event.data.type === 'CLEAR_CACHE') {
        event.waitUntil(
            caches.keys().then((cacheNames) => {
 return Promise.all(
 cacheNames
         .filter(cacheName => cacheName.startsWith('lagersystem-'))
         .map((cacheName) => {
         return caches.delete(cacheName);
             })
       );
            }).then(() => {
   return self.clients.matchAll();
            }).then((clients) => {
 clients.forEach(client => client.postMessage({ type: 'CACHE_CLEARED' }));
         })
    );
    }
});
