// Reusable modal dialog widget with header, body, and footer sections
// CSS is loaded via link tags in HTML head

export class DialogWidget {
    constructor(options = {}) {
        this.options = {
            title: options.title || '',
            content: options.content || '',
            onClose: options.onClose || null,
            ...options
        };
        
        this.dialog = null;
    }
    
    // Create and configure the dialog DOM element
    create() {
        if (this.dialog) return this.dialog;
        
        this.dialog = document.createElement('dialog');
        this.dialog.className = 'dialog-widget';
        this.dialog.innerHTML = `
            <div class="dialog-content">
                ${this.options.title ? `<div class="dialog-header"><button class="dialog-home-button" type="button">Home</button>${this.options.title}<button class="dialog-close-button" type="button">&times;</button></div>` : ''}
                <div class="dialog-body">${this.options.content}</div>
                <div class="dialog-footer">
                    <button class="dialog-close-bottom-button" type="button">Close</button>
                </div>
            </div>
        `;
        
        this.dialog.addEventListener('click', (e) => {
            if (e.target === this.dialog) this.close();
            if (e.target.classList.contains('dialog-close-button') || e.target.classList.contains('dialog-close-bottom-button')) {
                this.close();
            }
            if (e.target.classList.contains('dialog-home-button')) {
                this.home();
            }
        });
        
        this.dialog.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') this.close();
        });
        
        document.body.appendChild(this.dialog);
        return this.dialog;
    }
    
    // Open the dialog as a modal
    open() {
        if (!this.dialog) this.create();
        this.dialog.showModal();
    }
    
    // Close the dialog and trigger onClose callback
    close() {
        if (!this.dialog) return;
        this.dialog.close();
        if (this.options.onClose) this.options.onClose();
    }
    
    // Trigger the home button callback
    home() {
        if (this.options.onHome) this.options.onHome();
    }
    
    
    get isOpen() {
        return this.dialog?.open || false;
    }
    
    // Update the dialog body content dynamically
    updateContent(content) {
        const bodyEl = this.dialog?.querySelector('.dialog-body');
        if (bodyEl) bodyEl.innerHTML = content;
    }
    
    //not strictly necessary, but useful for cleanup
        /* Right now with just one persistent 
    dialog, destroy() isn't needed since the
    dialog is reused. But if we added more
    dialog types or dynamic dialogs, it would
    prevent DOM bloat and memory leaks. */
    // Remove dialog from DOM and clean up references
    destroy() {
        if (this.dialog) {
            this.dialog.remove();
            this.dialog = null;
        }
    }
}