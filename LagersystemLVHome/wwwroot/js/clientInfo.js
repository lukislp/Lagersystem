// Client info helper - collects browser info for session tracking
window.ClientInfo = {
    /**
 * Collects client information (IP is filled in on the server)
     * @returns {object} Client Info Objekt
     */
    async getClientInfo() {
        try {
            return {
            userAgent: navigator.userAgent || 'Unknown',
             language: navigator.language || 'Unknown',
       platform: navigator.platform || 'Unknown',
      screenWidth: screen.width,
       screenHeight: screen.height,
   timezoneOffset: new Date().getTimezoneOffset()
            };
        } catch (error) {
  console.error('Error getting client info:', error);
            return {
       userAgent: 'Unknown',
   language: 'Unknown',
       platform: 'Unknown',
       screenWidth: 0,
                screenHeight: 0,
        timezoneOffset: 0
};
   }
    },
    
    /**
     * Returns just the user agent (for quick access)
     * @returns {string} User-Agent String
     */
    getUserAgent() {
        return navigator.userAgent || 'Unknown';
 }
};
