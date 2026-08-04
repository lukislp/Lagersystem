// WebAuthn / Passkey JavaScript helper for LagerSystem
// Uses the Web Authentication API for secure passkey authentication

window.webAuthnHelper = {
    
    // Check whether WebAuthn is available
    isSupported: function() {
        return window.PublicKeyCredential !== undefined &&
               typeof window.PublicKeyCredential === 'function';
    },

    // Check whether Conditional UI (autofill) is supported
    isConditionalMediationSupported: async function() {
        if (!this.isSupported()) return false;
        try {
            return await PublicKeyCredential.isConditionalMediationAvailable();
        } catch {
         return false;
        }
    },

    // Check whether a platform authenticator is available (Touch ID, Face ID, Windows Hello)
    isPlatformAuthenticatorAvailable: async function() {
      if (!this.isSupported()) return false;
        try {
     return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
        } catch {
   return false;
        }
    },

    // ==================== REGISTRIERUNG ====================
    
    // Registers a new passkey
    registerPasskey: async function(options) {
        if (!this.isSupported()) {
    return { success: false, error: 'WebAuthn wird von diesem Browser nicht unterstützt.' };
     }

      try {
            // Convert Base64URL to ArrayBuffer
   const challenge = this.base64UrlToArrayBuffer(options.challenge);
   const userId = this.base64UrlToArrayBuffer(options.userId);

     // Bereite Exclude-Credentials vor
            const excludeCredentials = (options.excludeCredentials || []).map(cred => ({
           type: cred.type,
                id: this.base64UrlToArrayBuffer(cred.id),
    transports: cred.transports
            }));

 // Credential Creation Options
      const publicKeyCredentialCreationOptions = {
    challenge: challenge,
       rp: {
    name: options.rpName,
     id: options.rpId
            },
             user: {
     id: userId,
    name: options.userName,
          displayName: options.userDisplayName
          },
       pubKeyCredParams: [
     { type: 'public-key', alg: -7 },   // ES256 (ECDSA P-256)
         { type: 'public-key', alg: -257 }  // RS256 (RSASSA-PKCS1-v1_5)
    ],
                authenticatorSelection: {
         authenticatorAttachment: options.authenticatorSelection?.authenticatorAttachment || undefined,
       residentKey: options.authenticatorSelection?.residentKey || 'preferred',
    userVerification: options.authenticatorSelection?.userVerification || 'preferred',
           requireResidentKey: options.authenticatorSelection?.residentKey === 'required'
     },
        timeout: options.timeout || 300000,
     attestation: options.attestation || 'none',
      excludeCredentials: excludeCredentials.length > 0 ? excludeCredentials : undefined
    };

        // Create the credential
const credential = await navigator.credentials.create({
    publicKey: publicKeyCredentialCreationOptions
 });

       if (!credential) {
      console.error('No credential created');
        return { success: false, error: 'Keine Credential erstellt.' };
   }

     // Convert the response to Base64URL
     const response = {
     id: this.arrayBufferToBase64Url(credential.rawId),
       type: credential.type,
     response: {
                    clientDataJSON: this.arrayBufferToBase64Url(credential.response.clientDataJSON),
   attestationObject: this.arrayBufferToBase64Url(credential.response.attestationObject),
      transports: credential.response.getTransports ? credential.response.getTransports() : undefined
         },
deviceName: options.deviceName || this.detectDeviceName(),
  registeredFromIp: null, // Wird server-seitig gesetzt
       registeredUserAgent: navigator.userAgent
    };

            const jsonResponse = JSON.stringify(response);

            // IMPORTANT: return with the correct property name
            return { 
           success: true, 
          credential: jsonResponse,
          error: null
    };

        } catch (error) {
            console.error('WebAuthn registration error:', error);
     return { 
       success: false, 
    credential: null,
     error: this.getErrorMessage(error)
            };
        }
    },

    // ==================== AUTHENTIFIZIERUNG ====================

    // Authentifiziert mit einem Passkey
    authenticatePasskey: async function(options) {
        if (!this.isSupported()) {
     return { success: false, error: 'WebAuthn wird von diesem Browser nicht unterstützt.' };
        }

        try {
            // Konvertiere Challenge
    const challenge = this.base64UrlToArrayBuffer(options.challenge);

       // Bereite Allow-Credentials vor
let allowCredentials = undefined;
    if (options.allowCredentials && options.allowCredentials.length > 0) {
    allowCredentials = options.allowCredentials.map(cred => ({
    type: cred.type,
           id: this.base64UrlToArrayBuffer(cred.id),
         transports: cred.transports
    }));
            }

     // Credential Request Options
       const publicKeyCredentialRequestOptions = {
                challenge: challenge,
    rpId: options.rpId,
        timeout: options.timeout || 300000,
          userVerification: options.userVerification || 'preferred',
     allowCredentials: allowCredentials
            };

            // Request the credential
            const credential = await navigator.credentials.get({
              publicKey: publicKeyCredentialRequestOptions
            });

      if (!credential) {
       return { success: false, error: 'Keine Credential erhalten.' };
            }

  // Convert the response to Base64URL
            const response = {
  id: this.arrayBufferToBase64Url(credential.rawId),
        response: {
     clientDataJSON: this.arrayBufferToBase64Url(credential.response.clientDataJSON),
           authenticatorData: this.arrayBufferToBase64Url(credential.response.authenticatorData),
signature: this.arrayBufferToBase64Url(credential.response.signature),
         userHandle: credential.response.userHandle 
       ? this.arrayBufferToBase64Url(credential.response.userHandle) 
     : null
      }
     };

    return { success: true, credential: JSON.stringify(response) };

        } catch (error) {
            console.error('WebAuthn authentication error:', error);
            return { 
     success: false, 
       error: this.getErrorMessage(error)
         };
    }
    },

    // Conditional UI Authentication (Autofill)
 authenticateConditional: async function(options) {
        if (!await this.isConditionalMediationSupported()) {
            return { success: false, error: 'Conditional UI wird nicht unterstützt.' };
   }

        try {
            const challenge = this.base64UrlToArrayBuffer(options.challenge);

 const credential = await navigator.credentials.get({
      publicKey: {
                 challenge: challenge,
     rpId: options.rpId,
     timeout: options.timeout || 300000,
           userVerification: options.userVerification || 'preferred'
       },
           mediation: 'conditional'
   });

      if (!credential) {
     return { success: false, error: 'Keine Credential erhalten.' };
       }

     const response = {
           id: this.arrayBufferToBase64Url(credential.rawId),
           response: {
      clientDataJSON: this.arrayBufferToBase64Url(credential.response.clientDataJSON),
          authenticatorData: this.arrayBufferToBase64Url(credential.response.authenticatorData),
          signature: this.arrayBufferToBase64Url(credential.response.signature),
       userHandle: credential.response.userHandle 
           ? this.arrayBufferToBase64Url(credential.response.userHandle) 
          : null
          }
        };

            return { success: true, credential: JSON.stringify(response) };

        } catch (error) {
            console.error('WebAuthn conditional authentication error:', error);
   return { 
         success: false, 
       error: this.getErrorMessage(error)
            };
}
    },

    // ==================== HILFSFUNKTIONEN ====================

// Base64URL to ArrayBuffer
    base64UrlToArrayBuffer: function(base64url) {
        // Ersetze URL-safe Zeichen
 let base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
        // Add padding
        while (base64.length % 4) {
            base64 += '=';
    }
        const binaryString = atob(base64);
    const bytes = new Uint8Array(binaryString.length);
      for (let i = 0; i < binaryString.length; i++) {
         bytes[i] = binaryString.charCodeAt(i);
        }
        return bytes.buffer;
    },

    // ArrayBuffer to Base64URL
    arrayBufferToBase64Url: function(buffer) {
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
 }
        const base64 = btoa(binary);
    // Convert to URL-safe form
        return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
    },

    // Detects the device name automatically
    detectDeviceName: function() {
        const ua = navigator.userAgent;
        
        // Plattform erkennen
     if (/iPhone/.test(ua)) return 'iPhone';
        if (/iPad/.test(ua)) return 'iPad';
      if (/Mac/.test(ua) && navigator.maxTouchPoints > 0) return 'iPad';
        if (/Mac/.test(ua)) return 'Mac';
        if (/Android/.test(ua)) {
   if (/Mobile/.test(ua)) return 'Android Phone';
       return 'Android Tablet';
        }
        if (/Windows/.test(ua)) return 'Windows PC';
 if (/Linux/.test(ua)) return 'Linux PC';
        if (/CrOS/.test(ua)) return 'Chromebook';
        
      return 'Unbekanntes Gerät';
    },

    // Converts WebAuthn errors into user-friendly messages
    getErrorMessage: function(error) {
        if (error.name === 'NotAllowedError') {
       return 'Die Anfrage wurde abgebrochen oder der Benutzer hat die Berechtigung verweigert.';
     }
        if (error.name === 'InvalidStateError') {
   return 'Ein Passkey für dieses Gerät ist bereits registriert.';
        }
        if (error.name === 'NotSupportedError') {
      return 'Der Authenticator unterstützt diese Operation nicht.';
     }
 if (error.name === 'SecurityError') {
        return 'Die Sicherheitsanforderungen wurden nicht erfüllt. Stellen Sie sicher, dass Sie HTTPS verwenden.';
        }
        if (error.name === 'AbortError') {
     return 'Die Operation wurde abgebrochen.';
        }
        if (error.name === 'ConstraintError') {
    return 'Der Authenticator konnte die Anforderungen nicht erfüllen.';
        }
        if (error.name === 'UnknownError') {
            return 'Ein unbekannter Fehler ist aufgetreten.';
        }
        
        return error.message || 'Ein Fehler ist aufgetreten.';
    },

    // Returns the available authenticator types
    getAvailableAuthenticators: async function() {
        const result = {
         webAuthnSupported: this.isSupported(),
            platformAuthenticator: false,
     conditionalUI: false
     };

        if (result.webAuthnSupported) {
            result.platformAuthenticator = await this.isPlatformAuthenticatorAvailable();
            result.conditionalUI = await this.isConditionalMediationSupported();
        }

        return result;
    }
};
