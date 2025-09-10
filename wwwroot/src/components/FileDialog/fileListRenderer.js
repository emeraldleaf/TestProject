export class FileListRenderer {
    constructor() {
        this.eventHandlers = {};
    }
    
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
    
    formatFileSize(bytes) {
        if (bytes === 0) return '0 B';
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(1024));
        return Math.round(bytes / Math.pow(1024, i) * 10) / 10 + ' ' + sizes[i];
    }
    
    renderFileList(files) {
        if (!files || files.length === 0) {
            return '<div class="no-files">No files found</div>';
        }
        
        const rows = files.filter(file => file && file.name).map(file => `
            <div class="file-row" data-name="${this.escapeHtml(file.name)}" data-path="${this.escapeHtml(file.path || '')}" data-is-directory="${file.isDirectory}">
                <div class="file-info">
                    <div class="file-name">
                        ${file.isDirectory ? 
                            '<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor"><path d="M1.75 1A1.75 1.75 0 000 2.75v10.5C0 14.216.784 15 1.75 15h12.5A1.75 1.75 0 0016 13.25v-8.5A1.75 1.75 0 0014.25 3H7.5a.25.25 0 01-.2-.1l-.9-1.2C6.07 1.26 5.55 1 5 1H1.75z"/></svg>' : 
                            '<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor"><path d="M2 2a2 2 0 012-2h8a2 2 0 012 2v12a2 2 0 01-2 2H4a2 2 0 01-2-2V2zm2-1a1 1 0 00-1 1v12a1 1 0 001 1h8a1 1 0 001-1V2a1 1 0 00-1-1H4z"/></svg>'
                        } ${this.escapeHtml(file.name)}
                    </div>
                    <div class="file-size">${file.isDirectory ? '' : this.formatFileSize(file.size)}</div>
                </div>
            </div>
        `).join('');
        
        return `<div class="file-list">${rows}</div>`;
    }
    
    renderError(message) {
        return `<div class="error-message">Error: ${this.escapeHtml(message || 'Unknown error occurred')}</div>`;
    }
    
    renderSearching() {
        return `
            <div class="searching-message" style="text-align: center; padding: 20px; color: #666;">
                <div style="margin-bottom: 10px;"></div>
                <div>Searching files...</div>
            </div>
        `;
    }
    
    attachEventListeners(container) {
        container.addEventListener('click', (e) => {
            const row = e.target.closest('.file-row');
            if (row) {
                const file = {
                    name: row.dataset.name,
                    path: row.dataset.path,
                    isDirectory: row.dataset.isDirectory === 'true'
                };
                this.emit('file-click', file);
            }
        });
    }
    
    on(event, handler) {
        if (!this.eventHandlers[event]) this.eventHandlers[event] = [];
        this.eventHandlers[event].push(handler);
    }
    
    emit(event, ...args) {
        if (this.eventHandlers?.[event]) {
            this.eventHandlers[event].forEach(handler => handler(...args));
        }
    }
}