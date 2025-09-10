export class FileApiService {
    constructor() {
        // Use .NET API server when running on separate port
        this.baseUrl = window.location.port === '3000' ? 'http://localhost:5120' : '';
        this.cachedDefaultPath = null;
    }
    
    async getDefaultPath() {
        if (this.cachedDefaultPath) {
            return this.cachedDefaultPath;
        }
        
        try {
            const response = await fetch(`${this.baseUrl}/api/files/defaultpath`);
            if (response.ok) {
                const data = await response.json();
                this.cachedDefaultPath = data.defaultPath;
                return this.cachedDefaultPath;
            }
        } catch (error) {
            console.warn('Failed to get default path from server, using fallback');
        }
        
        // Fallback if server doesn't support the endpoint
        this.cachedDefaultPath = '/Users';
        return this.cachedDefaultPath;
    }
    
    async getFiles(directoryPath = null) {
        try {
            // Get default path from server if none provided
            const pathToUse = directoryPath || await this.getDefaultPath();
            const url = `${this.baseUrl}/api/files?path=${encodeURIComponent(pathToUse)}`;
                
            const response = await fetch(url);
            const data = await response.json();
            
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to fetch files');
            }
            
            return data;
        } catch (error) {
            throw new Error(`Error fetching files: ${error.message}`);
        }
    }

    async searchFiles(directoryPath, searchTerm, includeSubdirectories = true) {
        try {
            const url = `${this.baseUrl}/api/files/search?path=${encodeURIComponent(directoryPath)}&term=${encodeURIComponent(searchTerm)}&includeSubdirectories=${includeSubdirectories}`;
            const response = await fetch(url);
            const data = await response.json();
            
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to search files');
            }
            
            return data;
        } catch (error) {
            throw new Error(`Error searching files: ${error.message}`);
        }
    }

    downloadFile(filePath) {
        const url = `${this.baseUrl}/api/files/download?path=${encodeURIComponent(filePath)}`;
        window.open(url, '_blank');
    }

    async uploadFile(directoryPath, file) {
        try {
            const formData = new FormData();
            formData.append('file', file);
            
            const url = `${this.baseUrl}/api/files/upload?path=${encodeURIComponent(directoryPath)}`;
            const response = await fetch(url, {
                method: 'POST',
                body: formData
            });
            
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to upload file');
            }
            
            return data;
        } catch (error) {
            throw new Error(`Error uploading file: ${error.message}`);
        }
    }

    async copyFile(sourcePath, destinationPath) {
        try {
            const url = `${this.baseUrl}/api/files/copy?sourcePath=${encodeURIComponent(sourcePath)}&destinationPath=${encodeURIComponent(destinationPath)}`;
            const response = await fetch(url, {
                method: 'POST'
            });
            
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to copy file');
            }
            
            return data;
        } catch (error) {
            throw new Error(`Error copying file: ${error.message}`);
        }
    }

    async moveFile(sourcePath, destinationPath) {
        try {
            const url = `${this.baseUrl}/api/files/move?sourcePath=${encodeURIComponent(sourcePath)}&destinationPath=${encodeURIComponent(destinationPath)}`;
            const response = await fetch(url, {
                method: 'POST'
            });
            
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to move file');
            }
            
            return data;
        } catch (error) {
            throw new Error(`Error moving file: ${error.message}`);
        }
    }
}