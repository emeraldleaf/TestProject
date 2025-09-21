// Main file browser dialog component that manages file operations and UI state
import { DialogWidget } from './fileDialogWidget';
import { FileApiService } from './fileApiService';
import { FileListRenderer } from './fileListRenderer';
import { createFileBrowserTemplate } from './toolbarTemplate';
import { FileBrowserOptions, ActionMap, FileItem } from '../../types';

export class FileBrowserDialog {
    private options: FileBrowserOptions;
    private fileApiService: FileApiService;
    private fileRenderer: FileListRenderer;
    private currentPath: string | null = null;
    private defaultPath: string | null = null;
    private selectedFile: string | null = null;
    private actions: ActionMap;
    private dialog: DialogWidget;
    private searchKeyHandler?: (e: KeyboardEvent) => void;

    constructor(options: FileBrowserOptions = {}) {
        this.options = { title: 'File Browser', ...options };

        this.fileApiService = new FileApiService();
        this.fileRenderer = new FileListRenderer();
        
        this.actions = {
            'navigate-up': () => this.goUp(),
            'search': () => this.search(),
            'clear-search': () => this.clearSearch(),
            'upload': () => document.getElementById('file-upload').click(),
            'download': () => this.download(),
            'copy': () => this.copy(),
            'move': () => this.move()
        };
        
        this.dialog = new DialogWidget({
            id: 'file-browser-dialog',
            title: this.options.title,
            content: createFileBrowserTemplate(),
            className: 'file-browser',
            onHome: () => this.goHome(),
            onClose: () => this.options.onClose?.()
        });
        
        this.setupEventDelegation();
    }
    
    // Set up event delegation for toolbar actions and file operations
    setupEventDelegation(): void {
        document.addEventListener('click', async (e) => {
            if (!this.isOpen) return;
            
            const action = (e.target as HTMLElement).dataset.action;
            if (!action) return;
            
            e.preventDefault();
            
            try {
                if (this.actions?.[action]) {
                    await this.actions[action]();
                }
            } catch (error) {
                console.error('Action error:', error);
            }
        });
        
        document.addEventListener('change', async (e) => {
            if (!this.isOpen || (e.target as HTMLElement).id !== 'file-upload') return;
            
            try {
                await this.upload();
            } catch (error) {
                console.error('Upload error:', error);
            }
        });
    }
    
    // Configure Enter key handling for search input
    setupSearchInputHandler(): void {
        const searchInput = document.getElementById('search-input') as HTMLInputElement;
        if (searchInput) {
            searchInput.removeEventListener('keypress', this.searchKeyHandler);
            this.searchKeyHandler = (e) => {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    this.search();
                }
            };
            searchInput.addEventListener('keypress', this.searchKeyHandler);
        }
    }
    
    // Open the dialog and load initial file listing
    async open(initialPath: string | null = null): Promise<void> {
        this.dialog.open();
        await this.loadFiles(initialPath);
        this.setupSearchInputHandler();
    }
    
    close(): void {
        this.dialog.close();
    }
    
    get isOpen(): boolean {
        return this.dialog.isOpen;
    }
    
    // Load and display files for the specified directory
    async loadFiles(directoryPath: string | null = null): Promise<void> {
        const contentDiv = document.getElementById('file-browser-content');
        if (!contentDiv) return;

        try {
            const response = await this.fileApiService.getFiles(directoryPath);
            this.currentPath = response.directoryPath;
            
            if (this.defaultPath === null) {
                this.defaultPath = this.currentPath;
            }
            
            this.updateUrl(this.currentPath);
            this.updateFileSummary(response.files);
            
            contentDiv.innerHTML = this.fileRenderer.renderFileList(response.files, response.directoryPath);
            this.addFileClickHandlers(contentDiv);
            
        } catch (error) {
            contentDiv.innerHTML = this.fileRenderer.renderError(error.message);
            this.updateFileSummary([]);
        }
    }
    
    // Update the file count and size summary display
    updateFileSummary(files: FileItem[] | null, message: string | null = null): void {
        const summaryElement = document.getElementById('file-summary');
        if (!summaryElement) return;
        
        if (message) {
            summaryElement.textContent = message;
            return;
        }
        
        if (!files) return;
        
        const fileCount = files.filter(f => !f.isDirectory).length;
        const folderCount = files.filter(f => f.isDirectory).length;
        const totalSize = files
            .filter(f => !f.isDirectory && f.size > 0)
            .filter(f => !f.isDirectory && f.size > 0)
            .reduce((sum, f) => sum + f.size, 0);
        
        const sizeText = totalSize > 0 ? ` • ${this.formatSize(totalSize)}` : '';
        summaryElement.textContent = `${files.length} items (${fileCount} files, ${folderCount} folders)${sizeText}`;
    }
    
    // Format file size in human-readable units
    formatSize(bytes: number): string {
        if (bytes === 0) return '0 B';
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(1024));
        return Math.round(bytes / Math.pow(1024, i) * 10) / 10 + ' ' + sizes[i];
    }
    
    // Add click handlers for file selection and navigation
    addFileClickHandlers(contentDiv: HTMLElement): void {
        const rows = contentDiv.querySelectorAll('.file-row');
        rows.forEach(row => {
            row.addEventListener('click', async () => {
                rows.forEach(r => r.style.backgroundColor = '');
                row.style.backgroundColor = '#e3f2fd';
                
                if (row.dataset.isDirectory === "true") {
                    const searchInput = document.getElementById('search-input') as HTMLInputElement;
                    if (searchInput) searchInput.value = '';
                    await this.loadFiles(row.dataset.path);
                } else {
                    this.selectedFile = row.dataset.path;
                }
            });
            
            if (row.dataset.isDirectory === "false") {
                row.addEventListener('dblclick', () => {
                    this.fileApiService.downloadFile(row.dataset.path);
                });
            }
        });
    }
    
    getUrlParameter(name: string): string | null {
        return new URLSearchParams(window.location.search).get(name);
    }
    
    // Update browser URL for deep linking support
    updateUrl(path: string | null): void {
        const url = new URL(window.location);
        if (path) {
            url.searchParams.set('path', path);
        } else {
            url.searchParams.delete('path');
        }
        window.history.pushState({}, '', url);
    }
    
    // Perform file search with current term and scope settings
    async search(): Promise<void> {
        const searchInput = document.getElementById('search-input') as HTMLInputElement;
        const searchTerm = searchInput.value.trim();
        
        if (!searchTerm) {
            await this.loadFiles(this.currentPath);
            return;
        }
        
        const contentDiv = document.getElementById('file-browser-content');
        if (!contentDiv) return;
        
        try {
            this.updateFileSummary(null, 'Searching...');
            contentDiv.innerHTML = this.fileRenderer.renderSearching();
            
            const includeSubdirs = document.querySelector('input[name="search-scope"]:checked')?.value === 'subdirs';
            const response = await this.fileApiService.searchFiles(this.currentPath || '', searchTerm, includeSubdirs);
            
            this.updateFileSummary(response.files, response.errorMessage);
            contentDiv.innerHTML = this.fileRenderer.renderFileList(response.files, response.directoryPath);
            this.addFileClickHandlers(contentDiv);
            this.setupSearchInputHandler();
        } catch (error) {
            contentDiv.innerHTML = this.fileRenderer.renderError(error.message);
            this.updateFileSummary([]);
        }
    }
    
    async clearSearch(): Promise<void> {
        (document.getElementById('search-input') as HTMLInputElement).value = '';
        await this.loadFiles(this.currentPath);
        this.setupSearchInputHandler();
        this.setupSearchInputHandler();
    }
    
    // Handle file upload to current directory
    async upload(): Promise<void> {
        const fileInput = document.getElementById('file-upload') as HTMLInputElement;
        const file = fileInput.files[0];
        if (!file) return;
        
        try {
            await this.fileApiService.uploadFile(this.currentPath || '', file);
            await this.loadFiles(this.currentPath);
            fileInput.value = '';
        } catch (error) {
            alert(`Upload failed: ${error.message}`);
        }
    }
    
    async goHome(): Promise<void> {
        if (this.defaultPath) {
            await this.loadFiles(this.defaultPath);
        }
    }
    
    async goUp(): Promise<void> {
        if (!this.currentPath) return;
        
        const pathParts = this.currentPath.split(/[/\\]/).filter(part => part);
        if (pathParts.length <= 1) return;
        
        // Handle cross-platform path construction
        let parentPath;
        const remainingParts = pathParts.slice(0, -1);
        
        // Check if this is a Windows absolute path (has drive letter like "C:")
        const isWindowsPath = remainingParts.length > 0 && remainingParts[0].match(/^[A-Za-z]:$/);
        
        if (isWindowsPath) {
            // Windows: construct path without leading slash
            parentPath = remainingParts.join('/');
        } else {
            // Unix/Mac: add leading slash
            parentPath = '/' + remainingParts.join('/');
        }
        await this.loadFiles(parentPath);
    }
    
    download(): void {
        if (!this.selectedFile) {
            alert('Please select a file first');
            return;
        }
        this.fileApiService.downloadFile(this.selectedFile);
    }
    
    async copy(): Promise<void> {
        if (!this.selectedFile) {
            alert('Please select a file first');
            return;
        }
        
        const destination = prompt('Enter destination path:', this.selectedFile + '.copy');
        if (!destination) return;
        
        try {
            await this.fileApiService.copyFile(this.selectedFile, destination);
            await this.loadFiles(this.currentPath);
            alert('File copied successfully');
        } catch (error) {
            alert(`Copy failed: ${error.message}`);
        }
    }
    
    async move(): Promise<void> {
        if (!this.selectedFile) {
            alert('Please select a file first');
            return;
        }
        
        const destination = prompt('Enter destination path:', this.selectedFile);
        if (!destination) return;
        
        try {
            await this.fileApiService.moveFile(this.selectedFile, destination);
            await this.loadFiles(this.currentPath);
            this.selectedFile = null;
            alert('File moved successfully');
        } catch (error) {
            alert(`Move failed: ${error.message}`);
        }
    }
    
    // Configure browser navigation and URL handling
    setupBrowserIntegration(): void {
        window.addEventListener('popstate', async () => {
            if (this.isOpen) {
                await this.loadFiles(this.getUrlParameter('path'));
            }
        });
        
        window.addEventListener('DOMContentLoaded', () => {
            const urlPath = this.getUrlParameter('path');
            if (urlPath) this.open(urlPath);
        });
    }
}
