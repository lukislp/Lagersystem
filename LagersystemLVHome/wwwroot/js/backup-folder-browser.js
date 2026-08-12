// Folder browser for backup paths
window.BackupFolderBrowser = {
    /**
     * Opens a folder-selection dialog (modern browsers only)
     * @returns {Promise<object>} Selected path or null
     */
    async selectFolder() {
   try {
            if ('showDirectoryPicker' in window) {
    // Modern approach (Chrome 86+, Edge 86+)
        const dirHandle = await window.showDirectoryPicker({
     mode: 'readwrite',
            startIn: 'desktop'
 });
                
  // IMPORTANT: for security reasons browsers do NOT return the full path!
 // Show the user a warning and ask for manual input            
       return {
           success: true,
              // path: null - no full path available
    name: dirHandle.name,
      handle: dirHandle.name,
  needsManualInput: true, // Flag for manual input
            message: `Ordner "${dirHandle.name}" ausgewählt. Bitte geben Sie den vollständigen Pfad manuell ein.`
          };
        } else {
      // Fallback: Use input[type=file] with webkitdirectory
     return await this.selectFolderFallback();
        }
        } catch (error) {
          console.error('Folder selection error:', error);
     
          if (error.name === 'AbortError') {
          return { success: false, error: 'Abgebrochen' };
            }
     
      return { 
                success: false, 
          error: error.message || 'Ordner konnte nicht ausgewählt werden'
        };
    }
    },
    
    /**
  * Attempts to obtain the full path (usually does NOT work)
  */
    async getFullPath(dirHandle) {
        try {
          // This method does NOT work in most browsers
            // Browsers block access to full paths for security reasons
        if (dirHandle.getPath) {
     const paths = await dirHandle.getPath();
     return paths.join('/');
     }
     
            // Fallback: folder name only
     return dirHandle.name;
     } catch (error) {
  console.warn('Could not get full path (expected):', error);
         return null;
        }
    },
    
    /**
     * Fallback for older browsers (Chrome/Edge only)
     */
    selectFolderFallback() {
  return new Promise((resolve, reject) => {
            const input = document.createElement('input');
 input.type = 'file';
input.webkitdirectory = true;
   input.style.display = 'none';
          document.body.appendChild(input);
         
   input.onchange = () => {
         if (input.files && input.files.length > 0) {
            // Get folder path from first file
        const firstFile = input.files[0];
  const fullPath = firstFile.webkitRelativePath || firstFile.name;
     const folderName = fullPath.split('/')[0];
         
        resolve({
                success: true,
         path: null, // Again, no full path here
            name: folderName,
      fallback: true,
                needsManualInput: true,
      message: `Ordner "${folderName}" ausgewählt. Bitte geben Sie den vollständigen Pfad manuell ein.`
             });
    } else {
                    resolve({ success: false, error: 'Abgebrochen' });
                }
        
      document.body.removeChild(input);
         };
      
      input.oncancel = () => {
           resolve({ success: false, error: 'Abgebrochen' });
        document.body.removeChild(input);
            };
     
 input.click();
      });
    },
    
    /**
     * Checks whether the browser supports the folder browser
     */
    isSupported() {
 return 'showDirectoryPicker' in window || 
       ('HTMLInputElement' in window && 'webkitdirectory' in document.createElement('input'));
    },
    
    /**
     * Shows an info dialog for manual path entry
     */
    showManualInputInfo() {
        const message = `
⚠️ Browser-Sicherheit: Vollständiger Pfad nicht verfügbar

Aus Sicherheitsgründen geben moderne Browser den vollständigen
Ordnerpfad nicht zurück. Bitte geben Sie den Pfad manuell ein:

Windows:  C:\\Backups\\LagerSystem
Linux:    /home/user/backups
macOS:    /Users/username/backups

Netzwerk: \\\\server\\share\\backups

Tipp: Ordner im Explorer öffnen → Adressleiste kopieren
        `.trim();
        
        alert(message);
    },
 
    /**
     * Zeigt Dialog mit Pfad-Beispielen
     */
    showPathExamples() {
        const message = `
📁 Backup-Pfad Beispiele:

Windows Lokal:
  C:\\Backups\\LagerSystem
  D:\\Backup\\LagerSystem
  
Windows Netzwerk (UNC):
  \\\\nas\\backup\\LagerSystem
  \\\\server\\share\\backups
  
Linux:
  /home/user/backups/lagersystem
  /var/backups/lagersystem
  
macOS:
  /Users/username/backups
  /Volumes/Backup/LagerSystem

Tipp: Verwenden Sie einen dedizierten Backup-Ordner!
`.trim();
        
        alert(message);
    },
    
    /**
     * Validates a Windows/Linux path
     */
    validatePath(path) {
  if (!path || path.trim().length === 0) {
return { valid: false, error: 'Pfad darf nicht leer sein' };
    }
        
        // Windows path
    if (/^[A-Za-z]:\\/.test(path)) {
            return { valid: true, type: 'windows' };
   }
        
        // UNC path
        if (/^\\\\/.test(path)) {
            return { valid: true, type: 'unc' };
    }
        
      // Linux/Mac path
        if (path.startsWith('/')) {
            return { valid: true, type: 'unix' };
        }
  
        return { 
         valid: false, 
   error: 'Ungültiges Pfad-Format. Erwartete Formate: C:\\..., \\\\server\\... oder /...'
  };
    },
    
    /**
     * Formats a path for display
     */
formatPath(path) {
        if (!path) return '';
        
   // Shorten long paths
        const maxLength = 50;
    if (path.length > maxLength) {
const parts = path.split(/[\\\/]/);
            if (parts.length > 3) {
      return `${parts[0]}\\...\\${parts[parts.length - 1]}`;
 }
        }
     
        return path;
    },
    
    /**
     * Shows a help dialog with instructions
     */
    showHelpDialog() {
        const message = `
🔍 So finden Sie den richtigen Pfad:

1️⃣ Windows:
   - Ordner im Explorer öffnen
   - Adressleiste anklicken (oder Strg+L)
   - Pfad kopieren (z.B. C:\\Backups\\LagerSystem)
   - Hier einfügen

2️⃣ Netzwerk-Share:
   - Im Explorer Netzlaufwerk öffnen
 - UNC-Pfad kopieren (\\\\server\\share\\...)
   - Hier einfügen

3️⃣ Tipp:
   - Rechtsklik auf Ordner → "Als Pfad kopieren"
   - Dann hier einfügen

⚠️ Browser zeigen aus Sicherheitsgründen keine vollständigen
   Pfade an. Manuelle Eingabe ist erforderlich.
`.trim();
        
      alert(message);
    }
};

// Export for ES6 modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = window.BackupFolderBrowser;
}
