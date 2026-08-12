// Session monitor - periodically checks whether the session is still active
window.SessionMonitor = {
    intervalId: null,
    checkIntervalMs: 10000, // Check every 10 seconds
    dotNetHelper: null,
    sessionId: null,
    
    start: function(dotNetHelper, sessionId) {
        if (!sessionId) {
   console.warn('SessionMonitor: No sessionId provided - monitor not started');
  return;
        }
        
        if (!dotNetHelper) {
   console.warn('SessionMonitor: No dotNetHelper provided - monitor not started');
            return;
        }
        
  this.dotNetHelper = dotNetHelper;
        this.sessionId = sessionId;
        
  this.stop(); // Stop previous timer

 this.intervalId = setInterval(async () => {
          try {
                // Call the backend to check the session status
         const isActive = await this.dotNetHelper.invokeMethodAsync('CheckSessionStatus');

      if (!isActive) {
console.warn('Session is no longer active - initiating logout');
      this.stop();
     
        // Show warning
     alert('Your session has been terminated by an administrator. You will be logged out.');
     
      // Redirect to logout
           window.location.href = '/logout';
         }
    } catch (error) {
       console.error('Error checking session status:', error);
            }
     }, this.checkIntervalMs);
    },

    stop: function() {
        if (this.intervalId) {
         clearInterval(this.intervalId);
        this.intervalId = null;
        }
        this.dotNetHelper = null;
        this.sessionId = null;
    },

    setCheckInterval: function(intervalMs) {
        this.checkIntervalMs = intervalMs;
    }
};

// Cleanup on page unload
window.addEventListener('beforeunload', () => {
    if (window.SessionMonitor) {
        window.SessionMonitor.stop();
    }
});
