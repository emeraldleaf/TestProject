using TestProject.Models;

namespace TestProject.Services;

// Service contract for file system operations
public interface IFileService
{
    // Get files and directories in the specified path
    Task<FileListResponse> GetFilesAsync(string directoryPath);
    Task<FileListResponse> SearchFilesAsync(string directoryPath, string searchTerm, int maxResults, bool includeSubdirectories);
    // Download file as byte array (legacy - avoid for large files)
    Task<byte[]> DownloadFileAsync(string filePath);
    // Download file as stream for efficient memory usage
    Task<Stream> DownloadFileStreamAsync(string filePath);
    // Upload file content to directory
    Task<bool> UploadFileAsync(string directoryPath, string fileName, byte[] content);
    // Copy file from source to destination
    Task<bool> CopyFileAsync(string sourcePath, string destinationPath);
    // Move file from source to destination
    Task<bool> MoveFileAsync(string sourcePath, string destinationPath);
}