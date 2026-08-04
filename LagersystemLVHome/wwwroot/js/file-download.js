// File download helpers. Expose functions on window so they can be called
// safely via JSRuntime.InvokeVoidAsync("fileDownload.xxx", ...) without
// falling back to JavaScript eval and its XSS risks.

window.fileDownload = window.fileDownload || {};

/**
 * Download binary content encoded as base64.
 * @param {string} base64 - base64-encoded payload
 * @param {string} filename - name the browser should save the file as
 * @param {string} contentType - MIME type (e.g. "application/pdf")
 */
window.fileDownload.downloadBase64 = function (base64, filename, contentType) {
    const link = document.createElement('a');
    link.download = filename;
    link.href = `data:${contentType};base64,${base64}`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

/**
 * Download a plain text payload (JSON, CSV, HTML, etc).
 * The content is properly URL-encoded, avoiding string interpolation in
 * JavaScript eval.
 * @param {string} content - text payload
 * @param {string} filename - name the browser should save the file as
 * @param {string} contentType - MIME type (e.g. "application/json;charset=utf-8")
 * @param {boolean} addBom - prepend a UTF-8 BOM (needed for Excel CSV import)
 * @returns {boolean} true on success
 */
window.fileDownload.downloadText = function (content, filename, contentType, addBom) {
    try {
        const prefix = addBom ? '%EF%BB%BF' : '';
        const link = document.createElement('a');
        link.href = `data:${contentType},${prefix}${encodeURIComponent(content)}`;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        return true;
    } catch (error) {
        console.error('downloadText error:', error);
        return false;
    }
};

/**
 * Download HTML content and show a hint that the user can print to PDF.
 * @param {string} html - HTML content
 * @param {string} filename - name the browser should save the file as
 * @param {string} printHint - toast/alert text shown after download
 * @returns {boolean} true on success
 */
window.fileDownload.downloadHtmlWithPrintHint = function (html, filename, printHint) {
    const ok = window.fileDownload.downloadText(html, filename, 'text/html;charset=utf-8', false);
    if (ok && printHint) {
        setTimeout(function () { alert(printHint); }, 500);
    }
    return ok;
};

// Legacy global for old callers. Prefer window.fileDownload.downloadBase64.
function downloadFile(base64, filename, contentType) {
    window.fileDownload.downloadBase64(base64, filename, contentType);
}

