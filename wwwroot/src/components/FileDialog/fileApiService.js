// Service class for communicating with the file management REST API with advanced client-side caching
export class FileApiService {
    constructor() {
        // Use .NET API server when running on separate port
        this.baseUrl = window.location.port === '3000' ? 'http://localhost:5120' : '';

        // Advanced caching system
        this.cache = new Map();
        this.pendingRequests = new Map(); // Request deduplication
        this.cacheExpiration = new Map();
        this.invalidationQueue = new Set();
        this.invalidationTimers = new Map();

        // Cache configuration
        this.cacheConfig = {
            defaultPath: { ttl: 60 * 60 * 1000 }, // 1 hour
            fileList: { ttl: 30 * 1000 }, // 30 seconds
            search: { ttl: 60 * 1000 }, // 1 minute
        };

        // Legacy support
        this.cachedDefaultPath = null;
    }

    // Advanced caching helper methods
    getCacheKey(type, params = {}) {
        const paramString = Object.entries(params)
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([k, v]) => `${k}:${v}`)
            .join('|');
        return `${type}:${paramString}`;
    }

    isExpired(key) {
        const expiration = this.cacheExpiration.get(key);
        return !expiration || Date.now() > expiration;
    }

    setCacheItem(key, data, ttl) {
        this.cache.set(key, data);
        this.cacheExpiration.set(key, Date.now() + ttl);
    }

    getCacheItem(key) {
        if (this.isExpired(key)) {
            this.cache.delete(key);
            this.cacheExpiration.delete(key);
            return null;
        }
        return this.cache.get(key);
    }

    // Request deduplication - prevents multiple identical concurrent requests
    async withRequestDeduplication(key, requestFn) {
        // If there's already a pending request for this key, return that promise
        if (this.pendingRequests.has(key)) {
            return this.pendingRequests.get(key);
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
    async cachedFetch(url, cacheType, cacheParams = {}, options = {}) {
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
                    const errorData = await response.json().catch(() => ({}));
                    throw new Error(errorData.errorMessage || `HTTP ${response.status}: ${response.statusText}`);
                }

                const data = await response.json();

                // Cache the successful response
                this.setCacheItem(cacheKey, data, config.ttl);

                return data;
            } catch (error) {
                // On network error, try to return stale cache if available
                const staleData = this.cache.get(cacheKey);
                if (staleData) {
                    console.warn(`Network error, using stale cache for ${cacheKey}:`, error.message);
                    return staleData;
                }
                throw error;
            }
        });
    }

    // Cache invalidation methods with debouncing
    invalidateCache(pattern) {
        // Prevent duplicate invalidations for the same pattern
        if (this.invalidationQueue.has(pattern)) {
            return;
        }

        this.invalidationQueue.add(pattern);

        // Clear existing timer if one exists
        const existingTimer = this.invalidationTimers.get(pattern);
        if (existingTimer) {
            clearTimeout(existingTimer);
        }

        // Debounce invalidation to prevent excessive cache clearing
        const timer = setTimeout(() => {
            this.performCacheInvalidation(pattern);
            this.invalidationQueue.delete(pattern);
            this.invalidationTimers.delete(pattern);
        }, 100);

        this.invalidationTimers.set(pattern, timer);
    }

    // Perform the actual cache invalidation
    performCacheInvalidation(pattern) {
        const keysToDelete = [];
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

    clearCache() {
        this.cache.clear();
        this.cacheExpiration.clear();
        this.cachedDefaultPath = null;
    }

    // Get cache statistics for debugging
    getCacheStats() {
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
    async getDefaultPath() {
        // Check legacy cache first for backward compatibility
        if (this.cachedDefaultPath) {
            return this.cachedDefaultPath;
        }

        try {
            const url = `${this.baseUrl}/api/files/defaultpath`;
            const data = await this.cachedFetch(url, 'defaultPath');

            // Update legacy cache for backward compatibility
            this.cachedDefaultPath = data.defaultPath;
            return this.cachedDefaultPath;
        } catch (error) {
            console.warn('Failed to get default path from server, using fallback:', error.message);

            // Fallback if server doesn't support the endpoint - use allowed base path
            this.cachedDefaultPath = '/Users/joshuadell/dev';
            return this.cachedDefaultPath;
        }
    }
    
    // Fetch files and directories for the specified path with intelligent caching
    async getFiles(directoryPath = null) {
        try {
            // Get default path from server if none provided
            const pathToUse = directoryPath || await this.getDefaultPath();
            const url = `${this.baseUrl}/api/files?path=${encodeURIComponent(pathToUse)}`;

            // Use cached fetch with directory-specific caching
            const data = await this.cachedFetch(url, 'fileList', { path: pathToUse });

            return data;
        } catch (error) {
            throw new Error(`Error fetching files: ${error.message}`);
        }
    }

    // Search for files matching the term in the specified directory with caching
    async searchFiles(directoryPath, searchTerm, includeSubdirectories = true) {
        try {
            const url = `${this.baseUrl}/api/files/search?path=${encodeURIComponent(directoryPath)}&term=${encodeURIComponent(searchTerm)}&includeSubdirectories=${includeSubdirectories}`;

            // Cache search results based on all parameters
            const data = await this.cachedFetch(url, 'search', {
                path: directoryPath,
                term: searchTerm,
                includeSubdirs: includeSubdirectories
            });

            return data;
        } catch (error) {
            throw new Error(`Error searching files: ${error.message}`);
        }
    }

    // Initiate file download by opening in new tab
    downloadFile(filePath) {
        const url = `${this.baseUrl}/api/files/download?path=${encodeURIComponent(filePath)}`;
        window.open(url, '_blank');
    }

    // Upload a file to the specified directory with cache invalidation and idempotency
    async uploadFile(directoryPath, file, idempotencyKey) {
        // Create idempotency key based on file characteristics and destination
        const key = idempotencyKey || `upload:${directoryPath}:${file.name}:${file.size}:${file.lastModified}`;

        return this.withRequestDeduplication(key, async () => {
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

            // Invalidate cache for the affected directory
            this.invalidateCache(`fileList:*path:${directoryPath}`);

            return data;
        } catch (error) {
            throw new Error(`Error uploading file: ${error.message}`);
        }
        });
    }

    // Copy a file from source to destination path with cache invalidation and idempotency
    async copyFile(sourcePath, destinationPath, idempotencyKey) {
        // Create idempotency key based on source and destination paths
        const key = idempotencyKey || `copy:${sourcePath}:${destinationPath}`;

        return this.withRequestDeduplication(key, async () => {
        try {
            const url = `${this.baseUrl}/api/files/copy?sourcePath=${encodeURIComponent(sourcePath)}&destinationPath=${encodeURIComponent(destinationPath)}`;
            const response = await fetch(url, {
                method: 'POST'
            });

            const data = await response.json();
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
            throw new Error(`Error copying file: ${error.message}`);
        }
        });
    }

    // Move a file from source to destination path with cache invalidation and idempotency
    async moveFile(sourcePath, destinationPath, idempotencyKey) {
        // Create idempotency key based on source and destination paths
        const key = idempotencyKey || `move:${sourcePath}:${destinationPath}`;

        return this.withRequestDeduplication(key, async () => {
        try {
            const url = `${this.baseUrl}/api/files/move?sourcePath=${encodeURIComponent(sourcePath)}&destinationPath=${encodeURIComponent(destinationPath)}`;
            const response = await fetch(url, {
                method: 'POST'
            });

            const data = await response.json();
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
            throw new Error(`Error moving file: ${error.message}`);
        }
        });
    }

    // Additional utility methods for advanced usage

    // Force refresh a specific directory's cache with idempotency
    async refreshDirectory(directoryPath, idempotencyKey) {
        // Create idempotency key for refresh operations
        const key = idempotencyKey || `refresh:${directoryPath || 'default'}:${Date.now()}`;

        return this.withRequestDeduplication(key, async () => {
            this.invalidateCache(`fileList:*path:${directoryPath}`);
            return this.getFiles(directoryPath);
        });
    }

    // Preload a directory for faster navigation with idempotency
    async preloadDirectory(directoryPath, idempotencyKey) {
        // Create idempotency key for preload operations
        const key = idempotencyKey || `preload:${directoryPath || 'default'}`;

        return this.withRequestDeduplication(key, async () => {
            // Use a short-lived cache just for preloading
            const tempConfig = this.cacheConfig.fileList;
            this.cacheConfig.fileList = { ttl: 5 * 60 * 1000 }; // 5 minutes for preload

            try {
                await this.getFiles(directoryPath);
            } finally {
                this.cacheConfig.fileList = tempConfig;
            }
        });
    }

    // Get performance metrics for monitoring
    getPerformanceMetrics() {
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