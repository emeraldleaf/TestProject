using TestProject.Models;

namespace TestProject.Abstractions;

/// <summary>
/// Abstraction for file system operations to enable testing and flexibility
/// </summary>
public interface IFileSystemAdapter
{
    // Directory operations
    Task<bool> DirectoryExistsAsync(string path);
    Task<IEnumerable<FileSystemItemInfo>> GetDirectoryContentsAsync(string path);
    Task CreateDirectoryAsync(string path);

    // File operations
    Task<bool> FileExistsAsync(string path);
    Task<Stream> OpenFileForReadAsync(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task WriteAllBytesAsync(string path, byte[] content);
    Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = true);
    Task MoveFileAsync(string sourcePath, string destinationPath, bool overwrite = true);
    Task DeleteFileAsync(string path);

    // File info operations
    Task<FileSystemItemInfo> GetFileInfoAsync(string path);
    Task<long> GetFileSizeAsync(string path);
    Task<DateTime> GetLastWriteTimeAsync(string path);

    // Search operations
    Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption);
    Task<IEnumerable<string>> EnumerateDirectoriesAsync(string path, string searchPattern, SearchOption searchOption);
}

/// <summary>
/// File system item information
/// </summary>
public record FileSystemItemInfo(
    string Name,
    string FullPath,
    long Size,
    DateTime LastWriteTime,
    DateTime CreationTime,
    bool IsDirectory,
    FileAttributes Attributes
);