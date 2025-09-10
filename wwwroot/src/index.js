// Main entry point - imports and initializes modules
import { FileBrowserDialog } from './components/FileDialog/fileBrowserDialog.js';

// CSS files are loaded via link tags in HTML for development
// These imports are only used during build process

// Create file browser dialog instance
const fileBrowserDialog = new FileBrowserDialog({
    title: 'File Browser',
    onClose: () => {
        console.log('File browser closed');
    }
});

// Setup browser integration for URL handling
fileBrowserDialog.setupBrowserIntegration();

// Make function available globally for the HTML button
window.openDialog = async () => {
    // Check for path parameter in URL for deep linking
    const urlParams = new URLSearchParams(window.location.search);
    const urlPath = urlParams.get('path');
    await fileBrowserDialog.open(urlPath);
};