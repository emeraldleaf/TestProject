// Shared TypeScript interfaces and types for the file browser application

export interface FileItem {
    name: string;
    path: string;
    isDirectory: boolean;
    size: number;
}

export interface FileListResponse {
    files: FileItem[];
    directoryPath: string;
    errorMessage?: string;
}

export interface SearchResponse extends FileListResponse {
    searchTerm?: string;
    includeSubdirectories?: boolean;
}

export interface DialogOptions {
    id?: string;
    title?: string;
    content?: string;
    className?: string;
    onHome?: () => void;
    onClose?: () => void;
}

export interface FileBrowserOptions {
    title?: string;
    onClose?: () => void;
}

export interface CacheConfig {
    ttl: number;
}

export interface CacheStats {
    total: number;
    active: number;
    expired: number;
    pending: number;
    types: Record<string, number>;
}

export interface PerformanceMetrics extends CacheStats {
    hitRate: string;
    memoryUsage: {
        cache: number;
        expiration: number;
        pending: number;
    };
}

export type ActionFunction = () => void | Promise<void>;

export interface ActionMap {
    [key: string]: ActionFunction;
}

export type SearchScope = 'current' | 'subdirs';

declare global {
    interface Window {
        openDialog: () => Promise<void>;
    }
}