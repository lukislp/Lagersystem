// PWA Install Prompt - Global Component
// Zeigt Install-Button prominent auf allen Seiten an

class PWAInstallPrompt {
    constructor() {
        this.deferredPrompt = null;
     this.isInstalled = false;
        this.isInstallable = false;
        this.installButton = null;
        
        this.init();
    }

    init() {
        if (window.matchMedia('(display-mode: standalone)').matches || 
        window.navigator.standalone === true) {
     this.isInstalled = true;
        return;
   }

        // Listen for install prompt
 window.addEventListener('beforeinstallprompt', (e) => {
   e.preventDefault();
       this.deferredPrompt = e;
            this.isInstallable = true;
            this.showInstallButton();
     });

        // Listen for successful installation
      window.addEventListener('appinstalled', () => {
      this.isInstalled = true;
          this.hideInstallButton();
      if (window.showToast) {
   window.showToast('App erfolgreich installiert! 🎉', 'success', 5000);
            }
        });
        window.addEventListener('load', () => {
            if (this.isInstallable && !this.isInstalled) {
   this.showInstallButton();
     }
        });
  }

    showInstallButton() {
    // Remove existing button if any
        this.hideInstallButton();
        this.installButton = document.createElement('button');
        this.installButton.id = 'pwa-install-prompt';
        this.installButton.className = 'pwa-install-button';
        this.installButton.innerHTML = `
   <i class="bi bi-download me-2"></i>
    <span>Als App installieren</span>
      `;
        this.installButton.addEventListener('click', () => {
     this.promptInstall();
        });
 this.addStyles();
        document.body.appendChild(this.installButton);

        // Animate in
   setTimeout(() => {
       this.installButton.classList.add('show');
        }, 500);

     // Auto-hide after 10 seconds (but keep available on hover)
        setTimeout(() => {
            if (this.installButton && !this.installButton.matches(':hover')) {
  this.installButton.classList.add('minimized');
   }
        }, 10000);
    this.installButton.addEventListener('mouseenter', () => {
            this.installButton.classList.remove('minimized');
        });
    }

    hideInstallButton() {
 if (this.installButton) {
   this.installButton.remove();
     this.installButton = null;
        }
    }

    async promptInstall() {
  if (!this.deferredPrompt) {
    return;
        }
        this.deferredPrompt.prompt();

        // Wait for user choice
        const { outcome } = await this.deferredPrompt.userChoice;

        if (outcome === 'accepted') {
this.hideInstallButton();
  }
 this.deferredPrompt = null;
    }

    addStyles() {
        if (document.getElementById('pwa-install-styles')) {
            return;
     }

        const styles = document.createElement('style');
    styles.id = 'pwa-install-styles';
     styles.textContent = `
      .pwa-install-button {
 position: fixed;
            bottom: 20px;
            right: 20px;
    z-index: 9999;
          
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    color: white;
         border: none;
            border-radius: 50px;
     padding: 14px 28px;
     font-size: 16px;
        font-weight: 600;
                
      box-shadow: 0 8px 24px rgba(102, 126, 234, 0.4);
              cursor: pointer;
      
    display: flex;
  align-items: center;
   gap: 8px;
       
opacity: 0;
     transform: translateY(20px) scale(0.9);
  transition: all 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
            }

         .pwa-install-button.show {
                opacity: 1;
    transform: translateY(0) scale(1);
     }

            .pwa-install-button:hover {
  transform: translateY(-4px) scale(1.05);
         box-shadow: 0 12px 32px rgba(102, 126, 234, 0.5);
        }

   .pwa-install-button:active {
            transform: translateY(-2px) scale(1.02);
    }

   .pwa-install-button.minimized {
      width: 60px;
      height: 60px;
              padding: 0;
             border-radius: 50%;
    overflow: hidden;
            }

          .pwa-install-button.minimized span {
     display: none;
}

  .pwa-install-button.minimized i {
         margin: 0;
    font-size: 24px;
            }

            /* Mobile optimizations */
          @media (max-width: 768px) {
            .pwa-install-button {
          bottom: 80px;
         right: 16px;
padding: 12px 20px;
            font-size: 14px;
     }

          .pwa-install-button.minimized {
            width: 56px;
         height: 56px;
 }
            }

        /* Animation on page load */
      @keyframes bounce {
    0%, 20%, 50%, 80%, 100% {
     transform: translateY(0);
     }
  40% {
      transform: translateY(-10px);
  }
    60% {
 transform: translateY(-5px);
             }
     }

            .pwa-install-button.show {
     animation: bounce 2s ease-in-out 1s;
      }

            /* Pulse effect when visible */
         @keyframes pulse {
   0%, 100% {
        box-shadow: 0 8px 24px rgba(102, 126, 234, 0.4);
 }
     50% {
           box-shadow: 0 8px 32px rgba(102, 126, 234, 0.6);
                }
 }

            .pwa-install-button.show:not(.minimized) {
          animation: bounce 2s ease-in-out 1s, pulse 3s ease-in-out infinite 3s;
            }
        `;

        document.head.appendChild(styles);
    }
}

// Auto-initialize
let pwaInstallPrompt = null;

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
     pwaInstallPrompt = new PWAInstallPrompt();
    });
} else {
    pwaInstallPrompt = new PWAInstallPrompt();
}

// Export for Blazor interop
window.PWAInstallPrompt = {
    show: () => {
        if (pwaInstallPrompt) {
        pwaInstallPrompt.showInstallButton();
        }
  },
    hide: () => {
        if (pwaInstallPrompt) {
      pwaInstallPrompt.hideInstallButton();
  }
    },
    promptInstall: async () => {
   if (pwaInstallPrompt) {
    await pwaInstallPrompt.promptInstall();
     }
    }
};
