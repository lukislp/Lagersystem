// Quagga2-based Barcode Scanner for Blazor
let dotNetRef = null;
let quaggaStarted = false;
let codeHistory = {}; // Global code history
let currentStream = null;

export function init(dotNetReference) {
    dotNetRef = dotNetReference;
}

export async function toggleFullscreen(elementId) {
    try {
        const el = document.getElementById(elementId);
        if (!el) return false;
        const fsEl = el.closest('.scanner-viewport') || el;
        if (!document.fullscreenElement) {
            if (fsEl.requestFullscreen) {
                await fsEl.requestFullscreen();
            } else if (fsEl.webkitRequestFullscreen) {
                await fsEl.webkitRequestFullscreen();
            }
            return true;
        } else {
            if (document.exitFullscreen) {
                await document.exitFullscreen();
            } else if (document.webkitExitFullscreen) {
                await document.webkitExitFullscreen();
            }
            return false;
        }
    } catch (e) {
        console.error('Fullscreen toggle failed:', e);
        return false;
    }
}

export async function getAvailableCameras() {
    try {
  const devices = await navigator.mediaDevices.enumerateDevices();
    const cameras = devices
   .filter(device => device.kind === 'videoinput')
            .map((device, index) => ({
            deviceId: device.deviceId,
   label: device.label || `Kamera ${index + 1}`,
                kind: device.kind
        }));
        
     return JSON.stringify(cameras);
    } catch (error) {
        console.error('Error getting cameras:', error);
        return '[]';
    }
}

export async function startScanner(videoElementId, cameraId = '') {
    if (quaggaStarted) {
  stopScanner();
   await new Promise(resolve => setTimeout(resolve, 500)); // Warte auf Cleanup
    }

    try {
        await loadQuagga();
     codeHistory = {};
    
        // Camera constraints — keep a 16:9-ish ideal but do not force aspectRatio,
        // as some webcams refuse the constraint and silently never start.
        let constraints = {
            width: { min: 640, ideal: 1280, max: 1920 },
            height: { min: 360, ideal: 720, max: 1080 }
        };

        // If a specific camera was chosen
        if (cameraId) {
            constraints.deviceId = { exact: cameraId };
        } else {
            // Default: rear camera (ideal so desktop webcams still work)
            constraints.facingMode = { ideal: "environment" };
        }
    
        const config = {
     inputStream: {
                name: "Live",
       type: "LiveStream",
  target: document.getElementById(videoElementId),
     constraints: constraints,
                singleChannel: false
   },
            decoder: {
          readers: [
             "code_128_reader",
           "ean_reader",
      "ean_8_reader",
          "code_39_reader",
         "upc_reader",
          "upc_e_reader",
          "i2of5_reader"
    ],
       debug: {
       drawBoundingBox: true,
          showFrequency: false,
          drawScanline: true,
       showPattern: false
    },
        multiple: false
 },
            locate: true,
 locator: {
         halfSample: false,
    patchSize: "large"
         },
  frequency: 10,
            numOfWorkers: (typeof navigator !== 'undefined' && navigator.hardwareConcurrency) ? Math.min(4, navigator.hardwareConcurrency) : 2,
            willReadFrequently: true
        };

        // Pre-flight: explicitly ask for camera permission with the same
        // constraints so that a failure produces a real, useful error.
        try {
            const probeStream = await navigator.mediaDevices.getUserMedia({ video: constraints, audio: false });
            // We only needed it to surface permission/overconstrained errors — Quagga will open its own.
            probeStream.getTracks().forEach(t => t.stop());
        } catch (permErr) {
            console.error('getUserMedia failed:', permErr.name, permErr.message);
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnError', `${permErr.name}: ${permErr.message}`);
            }
            return;
        }

        const targetEl = document.getElementById(videoElementId);
        if (!targetEl) {
            const msg = `Target element #${videoElementId} not found`;
            console.error(msg);
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnError', msg);
            }
            return;
        }

 Quagga.init(config, (err) => {
   if (err) {
       console.error('Quagga init error:', err);
  if (dotNetRef) {
    dotNetRef.invokeMethodAsync('OnError', err.message || err.name || 'Kamera-Zugriff fehlgeschlagen');
         }
       return;
      }

  Quagga.start();
          quaggaStarted = true;

        // Speichere Stream-Referenz
 const video = document.querySelector(`#${videoElementId} video`);
          if (video && video.srcObject) {
      currentStream = video.srcObject;
   }

          // Sync the viewport aspect-ratio with the *actual* stream
          // resolution. Without this, object-fit:contain produces letterbox
          // bars and Quagga's percentage `area` no longer aligns with the
          // CSS `.scan-zone` overlay — the user would aim at one barcode
          // while Quagga inspects a slightly shifted region.
          const applyAspect = () => {
              if (!video) return;
              const w = video.videoWidth;
              const h = video.videoHeight;
              if (w > 0 && h > 0) {
                  const viewport = document.getElementById(videoElementId)?.closest('.scanner-viewport');
                  if (viewport) {
                      viewport.style.aspectRatio = `${w} / ${h}`;
                  }
              }
          };
          if (video) {
              if (video.readyState >= 1 && video.videoWidth > 0) {
                  applyAspect();
              } else {
                  video.addEventListener('loadedmetadata', applyAspect, { once: true });
              }
          }
        });

        // Visual feedback
Quagga.onProcessed((result) => {
    const drawingCtx = Quagga.canvas.ctx.overlay;
            const drawingCanvas = Quagga.canvas.dom.overlay;

            if (result) {
  if (result.boxes) {
   drawingCtx.clearRect(0, 0, 
          parseInt(drawingCanvas.getAttribute("width")), 
        parseInt(drawingCanvas.getAttribute("height")));
         result.boxes.filter(box => box !== result.box).forEach(box => {
         Quagga.ImageDebug.drawPath(box, { x: 0, y: 1 }, drawingCtx, 
{ color: "green", lineWidth: 2 });
       });
          }

         if (result.box) {
        Quagga.ImageDebug.drawPath(result.box, { x: 0, y: 1 }, drawingCtx, 
            { color: "#00F", lineWidth: 2 });
  }

          if (result.codeResult && result.codeResult.code) {
       Quagga.ImageDebug.drawPath(result.line, { x: 'x', y: 'y' }, drawingCtx, 
            { color: 'red', lineWidth: 3 });
      }
  }
        });

  // Barcode detection with validation
  let lastAcceptedCode = '';
        let lastAcceptedTime = 0;
        const DEBOUNCE_TIME = 2000;

Quagga.onDetected((result) => {
     if (!result || !result.codeResult || !result.codeResult.code) {
    return;
         }

      const code = result.codeResult.code;
            const format = result.codeResult.format;
         const now = Date.now();
       
     // Calculate average error
            const errors = result.codeResult.decodedCodes
      .filter(x => x.error !== undefined)
      .map(x => x.error);
const avgError = errors.length > 0 
        ? errors.reduce((a, b) => a + b, 0) / errors.length 
      : 0;

   if (!codeHistory[code]) {
      codeHistory[code] = { 
       count: 0, 
       errors: [], 
           firstSeen: now,
               lastSeen: now,
        format: format
};
        }
  
   // Increment count and add error
    codeHistory[code].count++;
       codeHistory[code].errors.push(avgError);
     codeHistory[code].lastSeen = now;
     
      // Calculate average error for this code
     const codeAvgError = codeHistory[code].errors.reduce((a, b) => a + b, 0) / codeHistory[code].errors.length;

        // Acceptance criteria — slightly relaxed so a clear barcode is
        // accepted on the very first frame, while noisy decodes still need
        // multiple consistent reads.
          const shouldAccept = 
     (codeAvgError < 0.10) ||
     (codeAvgError < 0.20 && codeHistory[code].count >= 2) ||
  (codeAvgError < 0.35 && codeHistory[code].count >= 3);

         if (!shouldAccept) {
      return;
   }

      // Check debounce
        if (code === lastAcceptedCode && (now - lastAcceptedTime) < DEBOUNCE_TIME) {
    return;
          }
   
            lastAcceptedCode = code;
          lastAcceptedTime = now;
      
      // Clean up old history entries
            Object.keys(codeHistory).forEach(key => {
      if (now - codeHistory[key].lastSeen > 10000) {
          delete codeHistory[key];
 }
            });

      if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnBarcodeDetected', code);
      }
        });

    } catch (error) {
        console.error('Scanner start error:', error);
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnError', error.message);
        }
    }
}

export function stopScanner() {
    if (!quaggaStarted) {
        return;
    }

    try {
        if (typeof Quagga !== 'undefined') {
          Quagga.stop();
       
 // Remove canvas elements
      const canvases = document.querySelectorAll('canvas.drawingBuffer');
            canvases.forEach(canvas => canvas.remove());
        }
        if (currentStream) {
currentStream.getTracks().forEach(track => {
       track.stop();
            });
            currentStream = null;
     }
        const videos = document.querySelectorAll('video');
        videos.forEach(video => {
            if (video.srcObject) {
    video.srcObject.getTracks().forEach(track => track.stop());
                video.srcObject = null;
            }
        });
        
        quaggaStarted = false;
        codeHistory = {};
    } catch (error) {
        console.error('Error stopping scanner:', error);
    }
}

async function loadQuagga() {
    if (typeof Quagga !== 'undefined') {
        return;
    }

    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
script.src = 'https://cdn.jsdelivr.net/npm/@ericblade/quagga2@1.8.4/dist/quagga.min.js';
        script.onload = () => {
     resolve();
        };
        script.onerror = () => reject(new Error('Failed to load Quagga2'));
document.head.appendChild(script);
});
}

export function dispose() {
    stopScanner();
    dotNetRef = null;
    codeHistory = {};
}
