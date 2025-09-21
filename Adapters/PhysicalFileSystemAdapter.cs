using TestProject.Abstractions;
using TestProject.Models;

namespace TestProject.Adapters;

/// <summary>
/// Production implementation that wraps the real file system
/// </summary>
public class PhysicalFileSystemAdapter : IFileSystemAdapter
{
    public async Task<bool> DirectoryExistsAsync(string path)
    {
        await Task.CompletedTask;
        return Directory.Exists(path);
    }

    public async Task<IEnumerable<FileSystemItemInfo>> GetDirectoryContentsAsync(string path)
    {
        return await Task.Run(() =>
        {
            var directoryInfo = new DirectoryInfo(path);
            var items = new List<FileSystemItemInfo>();

            foreach (var item in directoryInfo.GetFileSystemInfos())
            {
                var info = new FileSystemItemInfo(
                    Name: item.Name,
                    FullPath: item.FullName,
                    Size: item is FileInfo fileInfo ? fileInfo.Length : 0,
                    LastWriteTime: item.LastWriteTime,
                    CreationTime: item.CreationTime,
                    IsDirectory: item is DirectoryInfo,
                    Attributes: item.Attributes
                );
                items.Add(info);
            }

            return items;
        });
    }

    public async Task CreateDirectoryAsync(string path)
    {
        await Task.Run(() => Directory.CreateDirectory(path));
    }

    public async Task<bool> FileExistsAsync(string path)
    {
        await Task.CompletedTask;
        return File.Exists(path);
    }

    public async Task<Stream> OpenFileForReadAsync(string path)
    {
        return await Task.FromResult(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true));
    }

    public async Task<byte[]> ReadAllBytesAsync(string path)
    {
        return await File.ReadAllBytesAsync(path);
    }

    public async Task WriteAllBytesAsync(string path, byte[] content)
    {
        await File.WriteAllBytesAsync(path, content);
    }

    public async Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = true)
    {
        await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite));
    }

    public async Task MoveFileAsync(string sourcePath, string destinationPath, bool overwrite = true)
    {
        await Task.Run(() => File.Move(sourcePath, destinationPath, overwrite));
    }

    public async Task DeleteFileAsync(string path)
    {
        await Task.Run(() => File.Delete(path));
    }

    public async Task<FileSystemItemInfo> GetFileInfoAsync(string path)
    {
        return await Task.Run(() =>
        {
            var fileSystemInfo = File.Exists(path) ?
                (FileSystemInfo)new FileInfo(path) :
                new DirectoryInfo(path);

            return new FileSystemItemInfo(
                Name: fileSystemInfo.Name,
                FullPath: fileSystemInfo.FullName,
                Size: fileSystemInfo is FileInfo fi ? fi.Length : 0,
                LastWriteTime: fileSystemInfo.LastWriteTime,
                CreationTime: fileSystemInfo.CreationTime,
                IsDirectory: fileSystemInfo is DirectoryInfo,
                Attributes: fileSystemInfo.Attributes
            );
        });
    }

    public async Task<long> GetFileSizeAsync(string path)
    {
        return await Task.Run(() => new FileInfo(path).Length);
    }

    public async Task<DateTime> GetLastWriteTimeAsync(string path)
    {
        return await Task.Run(() => File.GetLastWriteTime(path));
    }

    public async Task<IEnumerable<string>> EnumerateFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        return await Task.Run(() => Directory.EnumerateFiles(path, searchPattern, searchOption));
    }

    public async Task<IEnumerable<string>> EnumerateDirectoriesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        return await Task.Run(() => Directory.EnumerateDirectories(path, searchPattern, searchOption));
    }
}