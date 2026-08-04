// QR-Code Helper Functions

/**
 * Download a base64 image as PNG file
 * @param {string} base64Image - Base64 encoded image
 * @param {string} fileName - Desired file name
 */
window.downloadBase64Image = function (base64Image, fileName) {
    const link = document.createElement('a');
    link.href = 'data:image/png;base64,' + base64Image;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

/**
 * Download a base64 file (generic for JSON, CSV, etc.)
 * @param {string} base64 - Base64 encoded file content
 * @param {string} fileName - Desired file name
 * @param {string} mimeType - MIME type of the file
 */
window.downloadBase64File = function (base64, fileName, mimeType) {
    try {
  const link = document.createElement('a');
    link.href = `data:${mimeType};base64,${base64}`;
    link.download = fileName;
  document.body.appendChild(link);

        link.click();

   document.body.removeChild(link);
    } catch (error) {
 console.error('[downloadBase64File] Error:', error);
   throw error;
    }
};

// Also make available without window. prefix for Blazor
downloadBase64File = window.downloadBase64File;
downloadBase64Image = window.downloadBase64Image;

/**
 * Print a QR code with optional label
 * @param {string} base64Image - Base64 encoded image
 * @param {string} label - Optional label text
 */
window.printQRCode = function (base64Image, label) {
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
     alert('Popup wurde blockiert. Bitte erlauben Sie Popups für diese Seite.');
        return;
    }

 printWindow.document.write(`
        <!DOCTYPE html>
        <html>
   <head>
    <title>QR-Code Drucken</title>
   <style>
       body {
      display: flex;
          flex-direction: column;
 justify-content: center;
  align-items: center;
  height: 100vh;
       margin: 0;
       font-family: Arial, sans-serif;
         }
         img {
          max-width: 500px;
           border: 2px solid #000;
          padding: 20px;
   }
           h2 {
margin-top: 20px;
    }
       </style>
        </head>
  <body>
  <img src="data:image/png;base64,${base64Image}" alt="QR Code" />
  ${label ? `<h2>${label}</h2>` : ''}
   </body>
    </html>
    `);

    printWindow.document.close();
    printWindow.focus();

    setTimeout(() => {
 printWindow.print();
  printWindow.close();
}, 250);
};

printQRCode = window.printQRCode;

/**
 * Print multiple QR codes in a grid layout
 * @param {Array} qrCodes - Array of {Image: base64, Label: string}
 */
window.printBatchQRCodes = function (qrCodes) {
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
     alert('Popup wurde blockiert. Bitte erlauben Sie Popups für diese Seite.');
        return;
  }

    let html = `
        <!DOCTYPE html>
     <html>
        <head>
 <title>Batch QR-Codes Drucken</title>
    <style>
     body {
    font-family: Arial, sans-serif;
        padding: 20px;
 }
       .qr-grid {
     display: grid;
        grid-template-columns: repeat(3, 1fr);
gap: 20px;
    page-break-inside: avoid;
       }
    .qr-item {
         text-align: center;
     border: 1px solid #ddd;
       padding: 10px;
     }
    .qr-item img {
        width: 200px;
     height: 200px;
 }
 .qr-label {
  margin-top: 10px;
      font-weight: bold;
         }
       @media print {
     .qr-item {
      page-break-inside: avoid;
         }
       }
 </style>
        </head>
 <body>
            <h1>QR-Codes</h1>
            <div class="qr-grid">
    `;

    qrCodes.forEach(qr => {
html += `
 <div class="qr-item">
  <img src="data:image/png;base64,${qr.Image}" alt="${qr.Label}" />
            <div class="qr-label">${qr.Label}</div>
    </div>
        `;
    });

    html += `
       </div>
  </body>
     </html>
  `;

    printWindow.document.write(html);
  printWindow.document.close();
    printWindow.focus();

setTimeout(() => {
     printWindow.print();
        printWindow.close();
    }, 250);
};

printBatchQRCodes = window.printBatchQRCodes;

/**
 * Download multiple QR codes as ZIP file
 * @param {Array} qrCodeData - Array of {FileName: string, Data: Uint8Array}
 * @param {string} zipFileName - Name of the ZIP file
 */
window.downloadQRCodesAsZip = async function (qrCodeData, zipFileName) {
    try {
        if (typeof JSZip === 'undefined') {
         console.error('JSZip library not loaded. Please include JSZip in your project.');
       alert('ZIP-Download-Funktion ist nicht verfügbar. Bitte laden Sie die Dateien einzeln herunter.');
 return;
      }

        const zip = new JSZip();
        qrCodeData.forEach(item => {
        zip.file(item.FileName, item.Data);
      });
      
   // Generate ZIP file
     const content = await zip.generateAsync({ type: 'blob' });
      const link = document.createElement('a');
  link.href = URL.createObjectURL(content);
        link.download = zipFileName;
     document.body.appendChild(link);
     link.click();
   document.body.removeChild(link);
      
    // Clean up
        URL.revokeObjectURL(link.href);
        
  } catch (error) {
        console.error('Error creating ZIP file:', error);
  alert('Fehler beim Erstellen der ZIP-Datei: ' + error.message);
    }
};

downloadQRCodesAsZip = window.downloadQRCodesAsZip;

/**
 * Copy QR code content to clipboard
 * @param {string} content - Content to copy
 */
window.copyQRContentToClipboard = function (content) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(content)
   .then(() => {
      })
       .catch(err => {
     console.error('Failed to copy content:', err);
        fallbackCopyToClipboard(content);
        });
    } else {
     fallbackCopyToClipboard(content);
  }
};

copyQRContentToClipboard = window.copyQRContentToClipboard;

/**
 * Fallback method for copying to clipboard
 * @param {string} text - Text to copy
 */
function fallbackCopyToClipboard(text) {
    const textArea = document.createElement('textarea');
    textArea.value = text;
    textArea.style.position = 'fixed';
    textArea.style.left = '-999999px';
    document.body.appendChild(textArea);
    textArea.focus();
  textArea.select();
 
    try {
        document.execCommand('copy');
    } catch (err) {
        console.error('Fallback copy failed:', err);
    }
    
  document.body.removeChild(textArea);
}

/**
 * Generate printable QR code sheet (e.g., for labels)
 * @param {Array} qrCodes - Array of QR codes with labels
 * @param {string} layout - Layout type: 'grid', 'list', 'labels'
 */
window.generatePrintableSheet = function (qrCodes, layout = 'grid') {
    const printWindow = window.open('', '_blank');
 
    let gridClass = 'qr-grid';
    let gridColumns = 'repeat(3, 1fr)';
    
    if (layout === 'list') {
  gridColumns = '1fr';
    } else if (layout === 'labels') {
     gridColumns = 'repeat(2, 1fr)';
    }
    
    const qrCodesHtml = qrCodes.map(qr => `
        <div class="qr-item">
          <img src="data:image/png;base64,${qr.Image}" class="qr-code" alt="${qr.Label}" />
    <div class="qr-label">${qr.Label}</div>
        </div>
 `).join('');
  
    const html = `
        <!DOCTYPE html>
        <html>
        <head>
  <title>QR-Codes Druckvorlage</title>
    <style>
       @page {
        size: A4;
       margin: 10mm;
    }
 body {
   font-family: Arial, sans-serif;
        margin: 0;
        padding: 0;
     }
       .qr-grid {
       display: grid;
   grid-template-columns: ${gridColumns};
     gap: 10px;
     padding: 5px;
           }
            .qr-item {
    text-align: center;
        page-break-inside: avoid;
     border: 1px dashed #ccc;
     padding: 8px;
          background: white;
    }
.qr-code {
     max-width: 100%;
      height: auto;
         border: 1px solid #000;
            padding: 5px;
 }
 .qr-label {
         font-size: 11px;
         font-weight: bold;
      margin-top: 5px;
   word-wrap: break-word;
 overflow-wrap: break-word;
       }
            </style>
   </head>
        <body>
   <div class="qr-grid">
         ${qrCodesHtml}
   </div>
 <script>
         window.onload = function() {
    window.print();
  window.onafterprint = function() {
   window.close();
         };
            };
  </script>
 </body>
        </html>
    `;
    
    printWindow.document.write(html);
    printWindow.document.close();
};

generatePrintableSheet = window.generatePrintableSheet;
