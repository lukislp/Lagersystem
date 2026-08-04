window.SetupFileUpload = window.SetupFileUpload || {
    dotNetHelper: null,
    initialized: false,
    initialize: function (dotNetHelper) {
     this.dotNetHelper = dotNetHelper;
    
 const fileInput = document.getElementById('backupFileInput');
    if (!fileInput) {
            console.error('SetupFileUpload: File input #backupFileInput not found!');
     return false;
        }

   // Remove old event listener if exists
        if (fileInput._setupFileUploadListener) {
            fileInput.removeEventListener('change', fileInput._setupFileUploadListener);
      }
      const changeHandler = async (e) => {
 const file = e.target.files[0];

 if (!file) {
return;
    }

   try {
         // Read file as base64
            const reader = new FileReader();
    
      reader.onload = async (event) => {
   const base64 = event.target.result.split(',')[1]; // Remove data:... prefix

     if (!dotNetHelper) {
    console.error('SetupFileUpload: dotNetHelper is null!');
              alert('Fehler: .NET Verbindung nicht verfügbar. Bitte Seite neu laden.');
        return;
        }
      
        try {
     // Call .NET method
       await dotNetHelper.invokeMethodAsync('HandleFileSelected', file.name, base64);
   } catch (invokeError) {
               console.error('SetupFileUpload: Error calling .NET method:', invokeError);
             alert('Fehler beim Upload: ' + invokeError.message);
         }
      };

    reader.onerror = function (error) {
     console.error('SetupFileUpload: FileReader error:', error);
        alert('Fehler beim Lesen der Datei: ' + error.message);
            };

        reader.readAsDataURL(file);
} catch (error) {
      console.error('SetupFileUpload: Error processing file:', error);
      alert('Fehler: ' + error.message);
            }
        };

        // Store listener reference and attach
        fileInput._setupFileUploadListener = changeHandler;
        fileInput.addEventListener('change', changeHandler);

        this.initialized = true;
        return true;
    },
    clear: function () {
      const fileInput = document.getElementById('backupFileInput');
        if (fileInput) {
            fileInput.value = '';
            if (fileInput._setupFileUploadListener) {
    fileInput.removeEventListener('change', fileInput._setupFileUploadListener);
        delete fileInput._setupFileUploadListener;
      }
        }
        this.dotNetHelper = null;
  this.initialized = false;
    }
};
