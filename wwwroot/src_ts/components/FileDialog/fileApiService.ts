import { FileListResponse, SearchResponse, CacheConfig, CacheStats, PerformanceMetrics } from '../../types';

// Service class for communicating with the file management REST API with advanced client-side caching
export class FileApiService {
    private baseUrl: string;
    private cache: Map<string, any> = new Map();
    private pendingRequests: Map<string, Promise<any>> = new Map();
    private cacheExpiration: Map<string, number> = new Map();
    private cacheConfig: Record<string, CacheConfig>;
    private cachedDefaultPath: string | null = null;

    constructor() {
        // Use .NET API server when running on separate port
        this.baseUrl = window.location.port === '3000' ? 'http://localhost:5120' : '';

        // Cache configuration
        this.cacheConfig = {
            defaultPath: { ttl: 60 * 60 * 1000 }, // 1 hour
            fileList: { ttl: 30 * 1000 }, // 30 seconds
            search: { ttl: 60 * 1000 }, // 1 minute
        };
    }

    // Advanced caching helper methods
    getCacheKey(type: string, params: Record<string, any> = {}): string {
        const paramString = Object.entries(params)
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([k, v]) => `${k}:${v}`)
            .join('|');
        return `${type}:${paramString}`;
    }

    isExpired(key: string): boolean {
        const expiration = this.cacheExpiration.get(key);
        return !expiration || Date.now() > expiration;
    }

    setCacheItem(key: string, data: any, ttl: number): void {
        this.cache.set(key, data);
        this.cacheExpiration.set(key, Date.now() + ttl);
    }

    getCacheItem(key: string): any | null {
        if (this.isExpired(key)) {
            this.cache.delete(key);
            this.cacheExpiration.delete(key);
            return null;
        }
        return this.cache.get(key);
    }

    // Request deduplication - prevents multiple identical concurrent requests
    async withRequestDeduplication<T>(key: string, requestFn: () => Promise<T>): Promise<T> {
        // If there's already a pending request for this key, return that promise
        if (this.pendingRequests.has(key)) {
            return this.pendingRequests.get(key) as Promise<T>;
        }

        // Create new request and store the promise
        const promise = requestFn().finally(() => {
            // Clean up the pending request when done
            this.pendingRequests.delete(key);
        });

        this.pendingRequests.set(key, promise);
        return promise;
    }

    // Enhanced fetch with caching and HTTP cache header support
    async cachedFetch(url: string, cacheType: string, cacheParams: Record<string, any> = {}, options: RequestInit = {}): Promise<any> {
        const cacheKey = this.getCacheKey(cacheType, { url, ...cacheParams });

        // Check memory cache first
        const cachedData = this.getCacheItem(cacheKey);
        if (cachedData) {
            return cachedData;
        }

        // Use request deduplication to prevent concurrent identical requests
        return this.withRequestDeduplication(cacheKey, async () => {
            const config = this.cacheConfig[cacheType];
            if (!config) {
                throw new Error(`Unknown cache type: ${cacheType}`);
            }

            // Add cache headers to leverage server-side caching
            const fetchOptions = {
                ...options,
                headers: {
                    'Cache-Control': 'max-age=0', // Always check with server first
                    'If-None-Match': '*', // Support ETag validation
                    ...options.headers
                }
            };

            try {
                const response = await fetch(url, fetchOptions);

                // Handle 304 Not Modified (server-side cache hit)
                if (response.status === 304) {
                    // Server says content hasn't changed, extend our cache
                    const existingData = this.cache.get(cacheKey);
                    if (existingData) {
                        this.setCacheItem(cacheKey, existingData, config.ttl);
                        return existingData;
                    }
                }

                if (!response.ok) {
                    const errorData = await response.json().catch(() => ({})) as any;
                    throw new Error((errorData as any).errorMessage || `HTTP ${response.status}: ${response.statusText}`);
                }

                const data = await response.json() as any;

                // Cache the successful response
                this.setCacheItem(cacheKey, data, config.ttl);

                return data;
            } catch (error) {
                // On network error, try to return stale cache if available
                const staleData = this.cache.get(cacheKey);
                if (staleData) {
                    console.warn(`Network error, using stale cache for ${cacheKey}:`, (error as Error).message);
                    return staleData;
                }
                throw error;
            }
        });
    }

    // Cache invalidation methods
    invalidateCache(pattern: string): void {
        const keysToDelete: string[] = [];
        for (const key of this.cache.keys()) {
            if (key.includes(pattern)) {
                keysToDelete.push(key);
            }
        }
        keysToDelete.forEach(key => {
            this.cache.delete(key);
            this.cacheExpiration.delete(key);
        });
    }

    clearCache(): void {
        this.cache.clear();
        this.cacheExpiration.clear();
        this.cachedDefaultPath = null;
    }

    // Get cache statistics for debugging
    getCacheStats(): CacheStats {
        const total = this.cache.size;
        const expired = Array.from(this.cache.keys()).filter(key => this.isExpired(key)).length;
        const pending = this.pendingRequests.size;

        return {
            total,
            active: total - expired,
            expired,
            pending,
            types: Object.fromEntries(
                Array.from(this.cache.keys())
                    .map(key => key.split(':')[0])
                    .reduce((acc, type) => acc.set(type, (acc.get(type) || 0) + 1), new Map())
            )
        };
    }
    
    // Get the server's configured default/base path with advanced caching
    async getDefaultPath(): Promise<string> {
        // Check legacy cache first for backward compatibility
        if (this.cachedDefaultPath) {
            return this.cachedDefaultPath ?? '';
        }

        try {
            const url = `${this.baseUrl}/api/files/defaultpath`;
            const data = await this.cachedFetch(url, 'defaultPath');

            // Update legacy cache for backward compatibility
            this.cachedDefaultPath = (data as any).defaultPath;
            return this.cachedDefaultPath !== null ? this.cachedDefaultPath : '';
        } catch (error) {
            console.warn('Failed to get default path from server, using fallback:', (error as Error).message);

            // Fallback if server doesn't support the endpoint - use allowed base path
            this.cachedDefaultPath = '/Users/joshuadell/dev';
            return this.cachedDefaultPath;
        }
    }
    
    // Fetch files and directories for the specified path with intelligent caching
    async getFiles(directoryPath: string | null = null): Promise<FileListResponse> {
        try {
            // Get default path from server if none provided
            const pathToUse = directoryPath || await this.getDefaultPath();
            const url = `${this.baseUrl}/api/files?path=${encodeURIComponent(pathToUse)}`;

            // Use cached fetch with directory-specific caching
            const data = await this.cachedFetch(url, 'fileList', { path: pathToUse });

            return data;
        } catch (error: any) {
            throw new Error(`Error fetching files: ${error.message}`);
        }
    }

    // Search for files matching the term in the specified directory with caching
    async searchFiles(directoryPath: string, searchTerm: string, includeSubdirectories: boolean = true): Promise<SearchResponse> {
        try {
            const url = `${this.baseUrl}/api/files/search?path=${encodeURIComponent(directoryPath)}&term=${encodeURIComponent(searchTerm)}&includeSubdirectories=${includeSubdirectories}`;

            // Cache search results based on all parameters
            const data = await this.cachedFetch(url, 'search', {
                path: directoryPath,
                term: searchTerm,
                includeSubdirs: includeSubdirectories
            });

            return data;
        } catch (error: any) {
            throw new Error(`Error searching files: ${error.message}`);
        }
    }

    // Initiate file download by opening in new tab
    downloadFile(filePath: string): void {
        const url = `${this.baseUrl}/api/files/download?path=${encodeURIComponent(filePath)}`;
        window.open(url, '_blank');
    }

    // Upload a file to the specified directory with cache invalidation
    async uploadFile(directoryPath: string, file: File): Promise<any> {
        try {
            const formData = new FormData();
            formData.append('file', file);

            const url = `${this.baseUrl}/api/files/upload?path=${encodeURIComponent(directoryPath)}`;
            const response = await fetch(url, {
                method: 'POST',
                body: formData
            });

            const data = await response.json() as any;
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to upload file');
            }

            // Invalidate cache for the affected directory
            if (directoryPath) {
                this.invalidateCache(`fileList:*path:${directoryPath}`);
            }

            return data;
        } catch (error) {
            throw new Error(`Error uploading file: ${(error as Error).message}`);
        }
    }

    // Copy a file from source to destination path with cache invalidation
    async copyFile(sourcePath: string, destinationPath: string): Promise<any> {
        try {
            const url = `${this.baseUrl}/api/files/copy?sourcePath=${encodeURIComponent(sourcePath)}&destinationPath=${encodeURIComponent(destinationPath)}`;
            const response = await fetch(url, {
                method: 'POST'
            });

            const data = await response.json() as any;
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to copy file');
            }

            // Invalidate cache for both source and destination directories
            const destDir = destinationPath.substring(0, destinationPath.lastIndexOf('/'));
            if (destDir) {
                this.invalidateCache(`fileList:*path:${destDir}`);
            }

            return data;
        } catch (error) {
            throw new Error(`Error copying file: ${(error as Error).message}`);
        }
    }

    // Move a file from source to destination path with cache invalidation
    async moveFile(sourcePath: string, destinationPath: string): Promise<any> {
        try {
            const url = `${this.baseUrl}/api/files/move?sourcePath=${encodeURIComponent(sourcePath)}&destinationPath=${encodeURIComponent(destinationPath)}`;
            const response = await fetch(url, {
                method: 'POST'
            });

            const data = await response.json() as any;
            if (!response.ok) {
                throw new Error(data.errorMessage || 'Failed to move file');
            }

            // Invalidate cache for both source and destination directories
            const sourceDir = sourcePath.substring(0, sourcePath.lastIndexOf('/'));
            const destDir = destinationPath.substring(0, destinationPath.lastIndexOf('/'));

            if (sourceDir) {
                this.invalidateCache(`fileList:*path:${sourceDir}`);
            }
            if (destDir && destDir !== sourceDir) {
                this.invalidateCache(`fileList:*path:${destDir}`);
            }

            return data;
        } catch (error) {
            throw new Error(`Error moving file: ${(error as Error).message}`);
        }
    }

    // Additional utility methods for advanced usage

    // Force refresh a specific directory's cache
    async refreshDirectory(directoryPath: string | null): Promise<FileListResponse> {
        if (directoryPath) {
            this.invalidateCache(`fileList:*path:${directoryPath}`);
        }
        return this.getFiles(directoryPath);
    }

    // Preload a directory for faster navigation
    async preloadDirectory(directoryPath: string | null): Promise<void> {
        // Use a short-lived cache just for preloading
        const tempConfig = this.cacheConfig.fileList;
        this.cacheConfig.fileList = { ttl: 5 * 60 * 1000 }; // 5 minutes for preload

        try {
            await this.getFiles(directoryPath);
        } finally {
            this.cacheConfig.fileList = tempConfig;
        }
    }

    // Get performance metrics for monitoring
    getPerformanceMetrics(): PerformanceMetrics {
        const cacheStats = this.getCacheStats();
        const hitRate = cacheStats.total > 0 ? ((cacheStats.total - cacheStats.expired) / cacheStats.total * 100).toFixed(1) : 0;

        return {
            ...cacheStats,
            hitRate: `${hitRate}%`,
            memoryUsage: {
                cache: this.cache.size,
                expiration: this.cacheExpiration.size,
                pending: this.pendingRequests.size
            }
        };
    }
}