// html5-qrcode based QR-Code Scanner for Blazor
let dotNetRef = null;
let html5QrCode = null;
let isScanning = false;
let lastScannedCode = '';
let lastScanTime = 0;
const DEBOUNCE_TIME = 2000; // 2 Sekunden zwischen gleichen Scans

export function init(dotNetReference) {
    dotNetRef = dotNetReference;
}

export async function startQRScanner(elementId) {
    if (isScanning) {
   return;
    }

    try {
    // Check whether html5-qrcode is available
        if (typeof Html5Qrcode === 'undefined') {
     throw new Error('html5-qrcode library not loaded');
        }

  // Create a new Html5Qrcode instance
        html5QrCode = new Html5Qrcode(elementId, {
      verbose: false,
      formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE]
        });

        // Kamera-Konfiguration
      const config = {
            fps: 10,
      qrbox: { width: 250, height: 250 },
            aspectRatio: 1.0,
 disableFlip: false,
   formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE]
        };

        // Erfolgs-Callback
 const qrCodeSuccessCallback = (decodedText, decodedResult) => {
         const now = Date.now();
            
            // Debounce - prevent scanning the same code multiple times
    if (decodedText === lastScannedCode && (now - lastScanTime) < DEBOUNCE_TIME) {
     return;
       }

        lastScannedCode = decodedText;
            lastScanTime = now;

     // Send to Blazor
            if (dotNetRef) {
         dotNetRef.invokeMethodAsync('OnQRCodeDetected', decodedText);
            }
        };

// Error callback (optional, for debugging)
        const qrCodeErrorCallback = (errorMessage) => {
    // Ignore frequent "NotFoundException" - that is normal when no QR code is visible
            if (!errorMessage.includes('NotFoundException')) {
           console.warn('QR-Code scan error:', errorMessage);
    }
        };
        await html5QrCode.start(
          { facingMode: "environment" }, // Rear camera
    config,
            qrCodeSuccessCallback,
            qrCodeErrorCallback
        );

 isScanning = true;

    } catch (error) {
        console.error('Error starting QR-Code scanner:', error);
  
        // Versuche Fallback mit vorderer Kamera
      if (error.name === 'OverconstrainedError' || error.name === 'NotFoundError') {
    try {
 await html5QrCode.start(
    { facingMode: "user" }, // Vorderkamera
          config,
        qrCodeSuccessCallback,
       qrCodeErrorCallback
       );
                isScanning = true;
 } catch (fallbackError) {
       console.error('Fallback failed:', fallbackError);
  if (dotNetRef) {
    dotNetRef.invokeMethodAsync('OnError', 'Kamera-Zugriff fehlgeschlagen. Bitte erlauben Sie den Kamera-Zugriff.');
     }
              throw fallbackError;
        }
        } else {
    if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnError', error.message || 'Scanner konnte nicht gestartet werden');
            }
            throw error;
        }
    }
}

export async function stopQRScanner() {
    if (!isScanning || !html5QrCode) {
        return;
    }

    try {
        await html5QrCode.stop();
        
        // Cleanup
        await html5QrCode.clear();
     
        isScanning = false;
      lastScannedCode = '';
        lastScanTime = 0;
    } catch (error) {
console.error('Error stopping scanner:', error);
    }
}

export async function getCameras() {
    try {
        if (typeof Html5Qrcode === 'undefined') {
            throw new Error('html5-qrcode library not loaded');
     }

        const cameras = await Html5Qrcode.getCameras();
return JSON.stringify(cameras);
    } catch (error) {
        console.error('Error getting cameras:', error);
    return '[]';
    }
}

export async function startQRScannerWithCamera(elementId, cameraId) {
    if (isScanning) {
        await stopQRScanner();
        await new Promise(resolve => setTimeout(resolve, 500));
    }

    try {
        if (typeof Html5Qrcode === 'undefined') {
            throw new Error('html5-qrcode library not loaded');
     }

      html5QrCode = new Html5Qrcode(elementId, {
            verbose: false,
  formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE]
      });

        const config = {
    fps: 10,
            qrbox: { width: 250, height: 250 },
       aspectRatio: 1.0,
       disableFlip: false,
            formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE]
        };

   const qrCodeSuccessCallback = (decodedText, decodedResult) => {
        const now = Date.now();
            
            if (decodedText === lastScannedCode && (now - lastScanTime) < DEBOUNCE_TIME) {
       return;
     }

    lastScannedCode = decodedText;
            lastScanTime = now;

   if (dotNetRef) {
 dotNetRef.invokeMethodAsync('OnQRCodeDetected', decodedText);
            }
        };

        const qrCodeErrorCallback = (errorMessage) => {
            if (!errorMessage.includes('NotFoundException')) {
     console.warn('QR-Code scan error:', errorMessage);
  }
     };

        await html5QrCode.start(
            cameraId,
       config,
   qrCodeSuccessCallback,
       qrCodeErrorCallback
   );

        isScanning = true;

    } catch (error) {
        console.error('Error starting QR-Code scanner:', error);
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnError', error.message || 'Scanner konnte nicht gestartet werden');
        }
        throw error;
    }
}

export function dispose() {
    stopQRScanner();
    dotNetRef = null;
    html5QrCode = null;
    isScanning = false;
    lastScannedCode = '';
    lastScanTime = 0;
}
