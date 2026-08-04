// Enhanced Camera Service for Barcode Scanner
class CameraService {
    constructor() {
        this.currentStream = null;
        this.currentDeviceId = null;
        this.availableDevices = [];
        this.torchSupported = false;
        this.torchEnabled = false;
    }

    async getAvailableDevices() {
        try {
  const devices = await navigator.mediaDevices.enumerateDevices();
        this.availableDevices = devices.filter(device => device.kind === 'videoinput');

            return this.availableDevices.map(device => ({
     deviceId: device.deviceId,
        label: device.label || `Camera ${this.availableDevices.indexOf(device) + 1}`,
       facingMode: this.guessFacingMode(device.label)
        }));
        } catch (error) {
            console.error('Error getting devices:', error);
          return [];
        }
    }

    guessFacingMode(label) {
        const lowerLabel = (label || '').toLowerCase();
     if (lowerLabel.includes('back') || lowerLabel.includes('rear') || lowerLabel.includes('environment')) {
            return 'environment';
        } else if (lowerLabel.includes('front') || lowerLabel.includes('user')) {
          return 'user';
        }
        return 'unknown';
    }

    async startCamera(deviceId = null, facingMode = 'environment') {
        try {
     this.stopCamera();

         const constraints = {
                video: deviceId ? 
          { deviceId: { exact: deviceId } } : 
    { facingMode: { ideal: facingMode } },
       audio: false
 };


        this.currentStream = await navigator.mediaDevices.getUserMedia(constraints);
            this.currentDeviceId = deviceId;

            // Check torch support
  const track = this.currentStream.getVideoTracks()[0];
     const capabilities = track.getCapabilities();
     this.torchSupported = 'torch' in capabilities;

            return {
        stream: this.currentStream,
              torchSupported: this.torchSupported
          };
        } catch (error) {
            console.error('Error starting camera:', error);
  throw error;
        }
    }

    stopCamera() {
        if (this.currentStream) {
  this.currentStream.getTracks().forEach(track => track.stop());
       this.currentStream = null;
this.torchEnabled = false;
        }
    }

    async toggleTorch() {
    if (!this.currentStream || !this.torchSupported) {
    console.warn('Torch not supported');
        return false;
        }

        try {
         const track = this.currentStream.getVideoTracks()[0];
            this.torchEnabled = !this.torchEnabled;
   
            await track.applyConstraints({
                advanced: [{ torch: this.torchEnabled }]
       });

   return this.torchEnabled;
        } catch (error) {
    console.error('Error toggling torch:', error);
return false;
}
    }

    async setZoom(zoomLevel) {
   if (!this.currentStream) {
            console.warn('No active camera stream');
      return false;
  }

        try {
 const track = this.currentStream.getVideoTracks()[0];
            const capabilities = track.getCapabilities();
        
            if ('zoom' in capabilities) {
      const min = capabilities.zoom.min || 1;
   const max = capabilities.zoom.max || 10;
   const zoom = Math.max(min, Math.min(max, zoomLevel));
            
     await track.applyConstraints({
      advanced: [{ zoom: zoom }]
       });

            return true;
    } else {
             console.warn('Zoom not supported');
         return false;
     }
        } catch (error) {
            console.error('Error setting zoom:', error);
    return false;
        }
    }

    getCurrentDevice() {
        return this.currentDeviceId;
    }

    isTorchEnabled() {
return this.torchEnabled;
    }

    isTorchSupported() {
        return this.torchSupported;
    }
}

// Global instance
window.cameraService = new CameraService();

// DotNet interop
window.getAvailableCameras = async () => {
    return await window.cameraService.getAvailableDevices();
};

window.startCamera = async (deviceId, facingMode = 'environment') => {
    return await window.cameraService.startCamera(deviceId, facingMode);
};

window.stopCamera = () => {
    window.cameraService.stopCamera();
};

window.toggleCameraTorch = async () => {
    return await window.cameraService.toggleTorch();
};

window.setCameraZoom = async (zoomLevel) => {
    return await window.cameraService.setZoom(zoomLevel);
};

window.isTorchSupported = () => {
    return window.cameraService.isTorchSupported();
};

window.isTorchEnabled = () => {
    return window.cameraService.isTorchEnabled();
};
