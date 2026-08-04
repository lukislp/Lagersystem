/**
 * Session Blocking Overlay - Pure JavaScript Implementation V3
 * 
 * This solution works independently of the Blazor lifecycle.
 * It checks every 2 seconds whether the session is still valid.
 * 
 * V3: Bessere Integration mit Blazor SPA-Navigation
 */

window.SessionBlockingOverlay = (function() {
    let checkInterval = null;
    let isBlocked = false;
    let currentSessionId = null;
    let checkFailCount = 0;
    let isInitialized = false;
    const MAX_CHECK_FAILURES = 3;
  const CHECK_INTERVAL_MS = 10000;
    
    // Paths where the overlay must NOT be active
    const SKIP_PATHS = ['/login', '/logout', '/register', '/setup', '/privacy', '/forgot-password', '/reset-password'];
    
    /**
   * Checks whether the current path should be skipped
     */
    function shouldSkipCurrentPath() {
  const currentPath = window.location.pathname.toLowerCase();
 return SKIP_PATHS.some(p => currentPath.startsWith(p));
    }
    
    /**
     * Initialisiert das Overlay-Monitoring
     */
    function init() {
      // Prevent duplicate initialisation
        if (isInitialized && checkInterval) {
   return;
  }

        // Skip auf Login/Logout/Register Seiten
        if (shouldSkipCurrentPath()) {
  return;
        }

        // Get session id from cookie
        currentSessionId = getSessionIdFromCookie();

        if (!currentSessionId) {
            return;
        }

        isInitialized = true;
        checkFailCount = 0;

        if (checkInterval) {
        clearInterval(checkInterval);
        }
        checkInterval = setInterval(checkSession, CHECK_INTERVAL_MS);

      // First check runs immediately (with a short delay for page build)
        setTimeout(checkSession, 500);
    }
  
    /**
     * Holt Session-ID aus Cookie
     */
    function getSessionIdFromCookie() {
        const cookies = document.cookie.split(';');
      for (let i = 0; i < cookies.length; i++) {
   const cookie = cookies[i].trim();
    if (cookie.startsWith('LagerSystem.SessionId=')) {
       return cookie.substring('LagerSystem.SessionId='.length);
            }
   }
    return null;
    }
    
    /**
     * Prueft Session-Status via API
     */
    async function checkSession() {
        if (isBlocked) return;
     
   // Check whether we're now on a skip page
        if (shouldSkipCurrentPath()) {
          return;
 }
        
     // Check whether the cookie still exists (it may have been removed by middleware)
    const cookieSessionId = getSessionIdFromCookie();
     
        if (!cookieSessionId) {
            showBlockingOverlay('SessionExpired');
 return;
        }

      // If the cookie changed (new login), refresh
        if (cookieSessionId !== currentSessionId) {
            currentSessionId = cookieSessionId;
        checkFailCount = 0;
     return;
      }
        
        try {
            const response = await fetch('/api/session/check/' + currentSessionId, {
                method: 'GET',
     headers: {
        'Accept': 'application/json'
          },
    credentials: 'same-origin'
     });
          
            if (!response.ok) {
 checkFailCount++;
     console.warn('SessionBlockingOverlay: API error ' + response.status + ' (attempt ' + checkFailCount + '/' + MAX_CHECK_FAILURES + ')');
 
          // After multiple failures: assume the session is invalid
                if (checkFailCount >= MAX_CHECK_FAILURES) {
       showBlockingOverlay('SessionExpired');
    }
      return;
            }
         checkFailCount = 0;

     const result = await response.json();

 if (!result.isActive) {
    showBlockingOverlay(result.reason);
   }
        } catch (error) {
checkFailCount++;

            // On network errors: give up after multiple attempts
      if (checkFailCount >= MAX_CHECK_FAILURES) {
 console.warn('SessionBlockingOverlay: Network errors, checking if cookie still exists...');
      // Check whether the cookie is still there
   if (!getSessionIdFromCookie()) {
    showBlockingOverlay('SessionExpired');
     }
            }
    }
    }
    
    /**
   * Zeigt das Blocking-Overlay an
     */
    function showBlockingOverlay(reason) {
if (isBlocked) return;
     isBlocked = true;

        if (checkInterval) {
            clearInterval(checkInterval);
            checkInterval = null;
        }
        isInitialized = false;
        
 // Loesche Session-Cookie
      document.cookie = 'LagerSystem.SessionId=; Path=/; Expires=Thu, 01 Jan 1970 00:00:01 GMT; Secure; SameSite=Lax';

        // Create the overlay
  const overlay = document.createElement('div');
        overlay.id = 'session-blocking-overlay-js';
    overlay.innerHTML = `
       <div class="session-blocking-modal-js">
     <div class="session-blocking-icon-js">
      <i class="bi bi-shield-lock"></i>
     </div>
   <h2>Sitzung beendet</h2>
      <p class="session-blocking-reason-js">${escapeHtml(getReasonText(reason))}</p>
       <p class="session-blocking-info-js">
       Ihre Sitzung wurde aus Sicherheitsgr\u00FCnden beendet. 
 Bitte melden Sie sich erneut an, um fortzufahren.
                </p>
          <button class="session-blocking-button-js" onclick="window.location.href='/login?reason=session-expired'">
      <i class="bi bi-box-arrow-in-right"></i>
          Neu anmelden
    </button>
         <div class="session-blocking-timestamp-js">
          <small>${new Date().toLocaleTimeString('de-DE')}</small>
      </div>
    </div>
    `;
  
        document.body.appendChild(overlay);
    }
    
    /**
     * Converts the reason enum to the German user-facing text
     */
    function getReasonText(reason) {
     const reasons = {
     'UserLogout': 'Sie haben sich abgemeldet.',
   'AdminForceLogout': 'Ein Administrator hat Ihre Sitzung beendet.',
            'Timeout': 'Ihre Sitzung ist wegen Inaktivit\u00E4t abgelaufen.',
            'ConcurrentLogin': 'Sie wurden abgemeldet, weil Sie sich an einem anderen Ger\u00E4t angemeldet haben.',
            'SuspiciousActivity': 'Ihre Sitzung wurde aus Sicherheitsgr\u00FCnden beendet.',
        'SessionExpired': 'Ihre Sitzung ist abgelaufen.',
            'SystemShutdown': 'Das System wurde heruntergefahren.',
   'NotFound': 'Ihre Sitzung wurde nicht gefunden.'
  };
   return reasons[reason] || 'Ihre Sitzung wurde beendet: ' + reason;
    }
    
    /**
     * HTML escaping for safety
 */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
    
    /**
     * Stoppt das Monitoring
     */
    function stop() {
        if (checkInterval) {
        clearInterval(checkInterval);
            checkInterval = null;
        }
        isBlocked = false;
     isInitialized = false;
        currentSessionId = null;
        checkFailCount = 0;

  // Remove overlay if present
        const overlay = document.getElementById('session-blocking-overlay-js');
        if (overlay) {
  overlay.remove();
    }
 }
    
    /**
     * Restart (e.g. after login or navigation)
     */
    function restart() {
        stop();
      setTimeout(init, 200); // Short delay for DOM update
    }
    
    /**
     * Invoked by Blazor after navigation
  * Can also be invoked manually
     */
    function onNavigated() {
        // Check whether we switched from a skip page to a normal page
        const hasSession = !!getSessionIdFromCookie();
  const shouldRun = !shouldSkipCurrentPath() && hasSession;
    
      if (shouldRun && !isInitialized) {
            init();
 } else if (!shouldRun && isInitialized) {
     stop();
        }
    }
    
    // ========================================
    // AUTO-INITIALIZATION
    // ========================================
    
    // On DOMContentLoaded, or immediately if the DOM is already loaded
    function autoStart() {
    // Wait until Blazor has loaded
        setTimeout(function() {
            init();
  
   // Start polling for URL changes (fallback for Blazor navigation)
  setInterval(function() {
    if (!isBlocked) {
 const hasSession = !!getSessionIdFromCookie();
          const shouldRun = !shouldSkipCurrentPath() && hasSession;
         
       if (shouldRun && !isInitialized) {
       init();
   }
            }
     }, 1000);
        }, 800);
    }
    
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', autoStart);
    } else {
  autoStart();
  }
    
    // Lausche auf Blazor-Navigation Events
    document.addEventListener('enhancednavigation', function() {
        setTimeout(onNavigated, 100);
    });
  
    // Fallback: History API Events
    window.addEventListener('popstate', function() {
        setTimeout(onNavigated, 100);
    });
    
    // Public API
    return {
        init: init,
  stop: stop,
        restart: restart,
        check: checkSession,
   onNavigated: onNavigated
    };
})();
