// Cookie helper for session-id storage (mobile-compatible)
// Works around the "Headers are read-only" issue in Blazor Server

window.cookieHelper = {
    /**
     * Setzt ein Cookie clientseitig (umgeht Blazor Server Response-Timing-Problem)
     * @param {string} name - Cookie-Name
     * @param {string} value - Cookie-Wert
     * @param {number} days - validity in days
     * @param {string} [sameSite='Lax'] - SameSite attribute (Lax, Strict or None)
     */
    setCookie: function(name, value, days, sameSite) {
        try {
    const expires = new Date();
            expires.setTime(expires.getTime() + (days * 24 * 60 * 60 * 1000));

            // Secure only under HTTPS (for local HTTP development)
            const isSecure = window.location.protocol === 'https:';
    const secureFlag = isSecure ? '; Secure' : '';
            const sameSiteValue = sameSite || 'Lax';

 const cookie = `${name}=${value}; expires=${expires.toUTCString()}; path=/; SameSite=${sameSiteValue}${secureFlag}`;

     document.cookie = cookie;

     return true;
   } catch (error) {
console.error('Error setting cookie:', error);
       return false;
        }
    },

 /**
     * Liest ein Cookie
     * @param {string} name - Cookie-Name
     * @returns {string|null} Cookie-Wert oder null
     */
    getCookie: function(name) {
        try {
  const nameEQ = name + "=";
       const ca = document.cookie.split(';');
            
            for (let i = 0; i < ca.length; i++) {
         let c = ca[i];
        while (c.charAt(0) === ' ') c = c.substring(1, c.length);
      if (c.indexOf(nameEQ) === 0) {
    const value = c.substring(nameEQ.length, c.length);
   return value;
                }
            }
  
            return null;
        } catch (error) {
            console.error('Error reading cookie:', error);
         return null;
   }
    },

    /**
     * Deletes a cookie
     * @param {string} name - Cookie-Name
     */
    deleteCookie: function(name) {
        try {
       document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;`;
       return true;
     } catch (error) {
            console.error('Error deleting cookie:', error);
   return false;
        }
    },

    /**
     * Checks whether a cookie exists
   * @param {string} name - Cookie-Name
 * @returns {boolean} true if the cookie exists
     */
    hasCookie: function(name) {
        return this.getCookie(name) !== null;
 }
};

