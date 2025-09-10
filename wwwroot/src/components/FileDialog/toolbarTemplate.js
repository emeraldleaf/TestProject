export function createFileBrowserTemplate() {
    return `
        <div class="file-browser-header">
            <div id="file-summary" class="file-summary-top">Loading...</div>
            <div class="file-toolbar">
                <button data-action="navigate-up" class="toolbar-button">↑ Up</button>
                <input type="text" id="search-input" placeholder="Search files..." class="search-input">
                <div class="search-scope-controls">
                    <label><input type="radio" name="search-scope" value="current" checked> Search This directory</label>
                    <label><input type="radio" name="search-scope" value="subdirs"> Include subdirectories</label>
                </div>
            </div>
            <div class="file-toolbar">
                <button data-action="search" class="toolbar-button">Search</button>
                <button data-action="clear-search" class="toolbar-button">Clear</button>
                <input type="file" id="file-upload" class="file-upload-input">
                <button data-action="upload" class="toolbar-button">Upload</button>
                <button data-action="download" class="toolbar-button">Download</button>
                <button data-action="copy" class="toolbar-button">Copy</button>
                <button data-action="move" class="toolbar-button">Move</button>
            </div>
        </div>
        <div id="file-browser-content" class="file-content"></div>
    `;
}
