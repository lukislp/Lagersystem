// PWA Helper - Service Worker Registration & Management
// Version: 1.0.0

class PWAHelper {
    constructor() {
        this.registration = null;
        this.updateAvailable = false;
  this.deferredPrompt = null;
    }

    // Service Worker registrieren
    async register() {
if (!('serviceWorker' in navigator)) {
  console.warn('[PWA] Service Worker not supported');
    return false;
        }

        try {
       this.registration = await navigator.serviceWorker.register('/service-worker.js', {
         scope: '/'
            });

 // Update Handler
         this.registration.addEventListener('updatefound', () => {
     this.handleUpdate();
            });

          // Check for updates every 30 minutes
            setInterval(() => {
    this.registration.update();
            }, 30 * 60 * 1000);

            return true;
        } catch (error) {
        console.error('[PWA] Service Worker registration failed:', error);
   return false;
 }
    }

    // Update Handler
    handleUpdate() {
        const newWorker = this.registration.installing;

        newWorker.addEventListener('statechange', () => {
      if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
       this.updateAvailable = true;
this.showUpdateNotification();
 }
     });
    }

    // Update Notification anzeigen
    showUpdateNotification() {
        if (window.showToast) {
 window.showToast(
      'Ein Update ist verfügbar! Bitte aktualisieren Sie die Seite.',
           'info',
        10000
            );
        } else {
     if (confirm('Ein Update ist verfügbar! Möchten Sie die Seite aktualisieren?')) {
      this.applyUpdate();
    }
      }
    }

    // Update anwenden
    async applyUpdate() {
        if (!this.registration || !this.registration.waiting) {
       return;
        }

  // Tell the service worker to run skipWaiting()
        this.registration.waiting.postMessage({ type: 'SKIP_WAITING' });

        // Reload after a short delay
        setTimeout(() => {
            window.location.reload();
        }, 500);
    }

    // Install Prompt verwalten
  setupInstallPrompt() {
   window.addEventListener('beforeinstallprompt', (e) => {
            // Prevent default browser prompt
       e.preventDefault();
     this.deferredPrompt = e;
            this.showInstallButton();
        });

        // Track installation
   window.addEventListener('appinstalled', () => {
     if (window.showToast) {
     window.showToast('App wurde erfolgreich installiert!', 'success');
   }
  
  this.deferredPrompt = null;
        });
    }

    // Install Button anzeigen
    showInstallButton() {
  // Create the install button if not present
  if (document.getElementById('pwa-install-btn')) {
            return;
        }

    const button = document.createElement('button');
     button.id = 'pwa-install-btn';
      button.className = 'btn btn-primary position-fixed';
   button.style.cssText = 'bottom: 20px; right: 20px; z-index: 1000; box-shadow: 0 4px 12px rgba(0,0,0,0.3);';
        button.innerHTML = '<i class="bi bi-download me-2"></i>App installieren';
        
        button.addEventListener('click', () => {
          this.promptInstall();
     });

  document.body.appendChild(button);

        // Auto-hide after 10 seconds
      setTimeout(() => {
     button.style.transition = 'opacity 0.5s';
            button.style.opacity = '0';
    setTimeout(() => button.remove(), 500);
        }, 10000);
    }

    // Install Prompt anzeigen
    async promptInstall() {
    if (!this.deferredPrompt) {
    return;
  }
     this.deferredPrompt.prompt();

        // Wait for user choice
     const { outcome } = await this.deferredPrompt.userChoice;

        this.deferredPrompt = null;

        // Remove install button
        const button = document.getElementById('pwa-install-btn');
     if (button) {
     button.remove();
  }
    }

    // Push Notifications Setup
    async setupPushNotifications() {
      if (!('Notification' in window)) {
            console.warn('[PWA] Push notifications not supported');
            return false;
  }

    if (!this.registration) {
       console.warn('[PWA] Service Worker not registered');
     return false;
        }

  // Request permission
        const permission = await Notification.requestPermission();
 
        if (permission !== 'granted') {
   return false;
    }

   // Subscribe to push notifications
        try {
       const subscription = await this.registration.pushManager.subscribe({
       userVisibleOnly: true,
      applicationServerKey: this.urlBase64ToUint8Array(
 // VAPID public key (must be generated on the server)
        'YOUR_VAPID_PUBLIC_KEY_HERE'
    )
     });

  // Send subscription to server
    // await fetch('/api/push/subscribe', {
   //     method: 'POST',
 //     headers: { 'Content-Type': 'application/json' },
         //     body: JSON.stringify(subscription)
   // });

     return true;
 } catch (error) {
 console.error('[PWA] Push subscription failed:', error);
     return false;
        }
    }

    // Test Push Notification
    async testPushNotification(title = 'Test Benachrichtigung', body = 'Dies ist eine Test-Benachrichtigung') {
   if (!('Notification' in window)) {
            return;
        }

      const permission = await Notification.requestPermission();
        
      if (permission === 'granted') {
            new Notification(title, {
       body: body,
    icon: '/icons/icon-192x192.png',
         badge: '/icons/icon-72x72.png',
   vibrate: [200, 100, 200],
        tag: 'test-notification'
  });
        }
    }

    // Background Sync registrieren
    async registerBackgroundSync(tag = 'sync-data') {
        if (!this.registration) {
     return;
        }

        if ('sync' in this.registration) {
     try {
     await this.registration.sync.register(tag);
   } catch (error) {
  console.error('[PWA] Background sync failed:', error);
       }
        }
  }

  // Cache Management
async getCacheSize() {
   if (!('caches' in window)) {
       return 0;
      }

        const cacheNames = await caches.keys();
      let totalSize = 0;

        for (const name of cacheNames) {
            const cache = await caches.open(name);
            const keys = await cache.keys();
 
            for (const request of keys) {
        const response = await cache.match(request);
  if (response) {
          const blob = await response.blob();
     totalSize += blob.size;
        }
     }
        }

  return totalSize;
    }

    // Cache leeren
    async clearCache() {
        if (!('caches' in window)) {
            return;
        }

     const cacheNames = await caches.keys();
        
    for (const name of cacheNames) {
            await caches.delete(name);
        }

        if (window.showToast) {
            window.showToast('Cache wurde geleert!', 'success');
        }
    }

    // Status abrufen
    async getStatus() {
        const status = {
   serviceWorker: 'serviceWorker' in navigator,
    registered: !!this.registration,
       updateAvailable: this.updateAvailable,
   installable: !!this.deferredPrompt,
            notifications: 'Notification' in window,
   notificationPermission: 'Notification' in window ? Notification.permission : 'denied',
      online: navigator.onLine,
   cacheSize: await this.getCacheSize()
        };

        return status;
    }

    // Helper: base64 to Uint8Array
 urlBase64ToUint8Array(base64String) {
        const padding = '='.repeat((4 - base64String.length % 4) % 4);
  const base64 = (base64String + padding)
      .replace(/\-/g, '+')
            .replace(/_/g, '/');

   const rawData = window.atob(base64);
        const outputArray = new Uint8Array(rawData.length);

        for (let i = 0; i < rawData.length; ++i) {
        outputArray[i] = rawData.charCodeAt(i);
   }
  return outputArray;
  }
}

// Global Instance erstellen
window.pwaHelper = new PWAHelper();

// Auto-initialise once the DOM has loaded
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
   initPWA();
    });
} else {
    initPWA();
}

async function initPWA() {
    // Service Worker registrieren
    const registered = await window.pwaHelper.register();
    
    if (registered) {
   // Install Prompt Setup
        window.pwaHelper.setupInstallPrompt();
   
        // Online/Offline Events
        window.addEventListener('online', () => {
            if (window.showToast) {
    window.showToast('Verbindung wiederhergestellt!', 'success');
         }
        });

   window.addEventListener('offline', () => {
            if (window.showToast) {
       window.showToast('Offline-Modus aktiviert', 'warning');
            }
 });
    }
}

// Export for Blazor interop
window.PWA = {
    getStatus: async () => {
        return await window.pwaHelper.getStatus();
    },
    clearCache: async () => {
        await window.pwaHelper.clearCache();
    },
    testNotification: async (title, body) => {
  await window.pwaHelper.testPushNotification(title, body);
    },
    promptInstall: async () => {
        await window.pwaHelper.promptInstall();
    }
};
