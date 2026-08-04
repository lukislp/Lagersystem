// Small app-wide helpers for navigation and DOM operations so .razor
// components don't have to call JSRuntime.InvokeVoidAsync("eval", ...).
// Keeping all JS under named window.appHelper members removes the XSS
// surface that inline eval blocks introduce.

window.appHelper = window.appHelper || {};

/**
 * Hard-redirect the current window. Used in auth flows where we cannot
 * rely on Blazor's NavigationManager (for example after a logout circuit
 * has already been torn down).
 * @param {string} url - target URL
 */
window.appHelper.redirect = function (url) {
    if (typeof url !== 'string' || url.length === 0) {
        return;
    }
    window.location.href = url;
};

/**
 * Scroll an element to its bottom. Used by the AI chat view.
 * @param {string} selector - CSS selector of the scrollable container
 */
window.appHelper.scrollToBottom = function (selector) {
    const element = document.querySelector(selector);
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};

/**
 * Scroll an element into view.
 * @param {string} selector - CSS selector
 * @param {ScrollIntoViewOptions} [options] - optional scroll options
 */
window.appHelper.scrollIntoView = function (selector, options) {
    const element = document.querySelector(selector);
    if (element) {
        element.scrollIntoView(options || { behavior: 'smooth', block: 'start' });
    }
};

/**
 * Delete the session cookie and stop the session-blocking overlay if it is
 * running. Used in the logout flow.
 */
window.appHelper.cleanupSession = function () {
    if (window.cookieHelper && typeof window.cookieHelper.deleteCookie === 'function') {
        window.cookieHelper.deleteCookie('LagerSystem.SessionId');
    } else {
        document.cookie = 'LagerSystem.SessionId=; Path=/; Expires=Thu, 01 Jan 1970 00:00:01 GMT; SameSite=Lax';
    }

    if (window.SessionBlockingOverlay && typeof window.SessionBlockingOverlay.stop === 'function') {
        window.SessionBlockingOverlay.stop();
    }
};
