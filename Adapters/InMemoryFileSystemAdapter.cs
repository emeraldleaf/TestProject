using System.Collections.Concurrent;
using TestProject.Abstractions;
using TestProject.Models;

namespace TestProject.Adapters;

/// <summary>
/// In-memory file system implementation for testing
/// </summary>
public class InMemoryFileSystemAdapter : IFileSystemAdapter
{
    private readonly ConcurrentDictionary<string, InMemoryFileSystemItem> _items = new();
    private readonly object _lock = new object();

    public class InMemoryFileSystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public DateTime LastWriteTime { get; set; } = DateTime.UtcNow;
        public DateTime CreationTime { get; set; } = DateTime.UtcNow;
        public bool IsDirectory { get; set; }
        public FileAttributes Attributes { get; set; } = FileAttributes.Normal;
    }

    public InMemoryFileSystemAdapter()
    {
        // Create root directory
        _items.TryAdd("/", new InMemoryFileSystemItem
        {
            Name = "",
            FullPath = "/",
            IsDirectory = true,
            Attributes = FileAttributes.Directory
        });
    }

    private string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized == "/" ? "/" : normalized.TrimEnd('/');
    }

    private string GetParentPath(string path)
    {
        var normalized = NormalizePath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : normalized.Substring(0, lastSlash);
    }

    public async Task<bool> DirectoryExistsAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);
        return _items.TryGetValue(normalized, out var item) && item.IsDirectory;
    }

    public async Task<IEnumerable<FileSystemItemInfo>> GetDirectoryContentsAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        if (!await DirectoryExistsAsync(normalized))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var children = _items.Values
            .Where(item => GetParentPath(item.FullPath) == normalized && item.FullPath != normalized)
            .Select(item => new FileSystemItemInfo(
                Name: item.Name,
                FullPath: item.FullPath,
                Size: item.Content?.Length ?? 0,
                LastWriteTime: item.LastWriteTime,
                CreationTime: item.CreationTime,
                IsDirectory: item.IsDirectory,
                Attributes: item.Attributes
            ))
            .OrderBy(item => item.IsDirectory ? 0 : 1)
            .ThenBy(item => item.Name);

        return children;
    }

    public async Task CreateDirectoryAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        lock (_lock)
        {
            _items.TryAdd(normalized, new InMemoryFileSystemItem
            {
                Name = Path.GetFileName(normalized),
                FullPath = normalized,
                IsDirectory = true,
                Attributes = FileAttributes.Directory
            });
        }
    }

    public async Task<bool> FileExistsAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);
        return _items.TryGetValue(normalized, out var item) && !item.IsDirectory;
    }

    public async Task<Stream> OpenFileForReadAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        if (!_items.TryGetValue(normalized, out var item) || item.IsDirectory)
            throw new FileNotFoundException($"File not found: {path}");

        return new MemoryStream(item.Content);
    }

    public async Task<byte[]> ReadAllBytesAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        if (!_items.TryGetValue(normalized, out var item) || item.IsDirectory)
            throw new FileNotFoundException($"File not found: {path}");

        return item.Content.ToArray();
    }

    public async Task WriteAllBytesAsync(string path, byte[] content)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        lock (_lock)
        {
            _items.AddOrUpdate(normalized,
                new InMemoryFileSystemItem
                {
                    Name = Path.GetFileName(normalized),
                    FullPath = normalized,
                    Content = content.ToArray(),
                    IsDirectory = false,
                    CreationTime = DateTime.UtcNow,
                    LastWriteTime = DateTime.UtcNow
                },
                (key, existingItem) =>
                {
                    existingItem.Content = content.ToArray();
                    existingItem.LastWriteTime = DateTime.UtcNow;
                    return existingItem;
                });
        }
    }

    public async Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = true)
    {
        await Task.CompletedTask;
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDest = NormalizePath(destinationPath);

        if (!_items.TryGetValue(normalizedSource, out var sourceItem) || sourceItem.IsDirectory)
            throw new FileNotFoundException($"Source file not found: {sourcePath}");

        if (!overwrite && _items.ContainsKey(normalizedDest))
            throw new IOException($"Destination file already exists: {destinationPath}");

        lock (_lock)
        {
            _items.AddOrUpdate(normalizedDest,
                new InMemoryFileSystemItem
                {
                    Name = Path.GetFileName(normalizedDest),
                    FullPath = normalizedDest,
                    Content = sourceItem.Content.ToArray(),
                    IsDirectory = false,
                    CreationTime = DateTime.UtcNow,
                    LastWriteTime = DateTime.UtcNow
                },
                (key, existingItem) =>
                {
                    existingItem.Content = sourceItem.Content.ToArray();
                    existingItem.LastWriteTime = DateTime.UtcNow;
                    return existingItem;
                });
        }
    }

    public async Task MoveFileAsync(string sourcePath, string destinationPath, bool overwrite = true)
    {
        await CopyFileAsync(sourcePath, destinationPath, overwrite);
        await DeleteFileAsync(sourcePath);
    }

    public async Task DeleteFileAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        lock (_lock)
        {
            _items.TryRemove(normalized, out _);
        }
    }

    public async Task<FileSystemItemInfo> GetFileInfoAsync(string path)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        if (!_items.TryGetValue(normalized, out var item))
            throw new FileNotFoundException($"Path not found: {path}");

        return new FileSystemItemInfo(
            Name: item.Name,
            FullPath: item.FullPath,
            Size: item.Content?.Length ?? 0,
            LastWriteTime: item.LastWriteTime,
            CreationTime: item.CreationTime,
            IsDirectory: item.IsDirectory,
            Attributes: item.Attributes
        );
    }

    public async Task<long> GetFileSizeAsync(string path)
    {
        var info = await GetFileInfoAsync(path);
        return info.Size;
    }

    public async Task<DateTime> GetLastWriteTimeAsync(string path)
    {
        var info = await GetFileInfoAsync(path);
        return info.LastWriteTime;
    }

    public async Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        var files = _items.Values
            .Where(item => !item.IsDirectory)
            .Where(item =>
            {
                if (searchOption == SearchOption.TopDirectoryOnly)
                    return GetParentPath(item.FullPath) == normalized;
                else
                    return item.FullPath.StartsWith(normalized + "/");
            })
            .Where(item => MatchesPattern(item.Name, searchPattern))
            .Select(item => item.FullPath);

        return files;
    }

    public async Task<IEnumerable<string>> EnumerateDirectoriesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        await Task.CompletedTask;
        var normalized = NormalizePath(path);

        var directories = _items.Values
            .Where(item => item.IsDirectory && item.FullPath != normalized)
            .Where(item =>
            {
                if (searchOption == SearchOption.TopDirectoryOnly)
                    return GetParentPath(item.FullPath) == normalized;
                else
                    return item.FullPath.StartsWith(normalized + "/");
            })
            .Where(item => MatchesPattern(item.Name, searchPattern))
            .Select(item => item.FullPath);

        return directories;
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (pattern == "*" || string.IsNullOrEmpty(pattern))
            return true;

        // Simple pattern matching - could be enhanced with regex
        if (pattern.StartsWith("*") && pattern.EndsWith("*"))
        {
            var middle = pattern.Substring(1, pattern.Length - 2);
            return name.Contains(middle, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.StartsWith("*"))
        {
            var suffix = pattern.Substring(1);
            return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern.Substring(0, pattern.Length - 1);
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    // Test utility methods
    public void AddFile(string path, byte[] content)
    {
        var normalized = NormalizePath(path);
        _items.TryAdd(normalized, new InMemoryFileSystemItem
        {
            Name = Path.GetFileName(normalized),
            FullPath = normalized,
            Content = content.ToArray(),
            IsDirectory = false,
            CreationTime = DateTime.UtcNow,
            LastWriteTime = DateTime.UtcNow
        });
    }

    public void AddFile(string path, string content) => AddFile(path, System.Text.Encoding.UTF8.GetBytes(content));

    public void AddDirectory(string path)
    {
        CreateDirectoryAsync(path).Wait();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            // Re-add root
            _items.TryAdd("/", new InMemoryFileSystemItem
            {
                Name = "",
                FullPath = "/",
                IsDirectory = true,
                Attributes = FileAttributes.Directory
            });
        }
    }
}