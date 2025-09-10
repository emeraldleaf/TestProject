using TestProject.Models;

namespace TestProject.Services;

public interface IFileService
{
    Task<FileListResponse> GetFilesAsync(string directoryPath);
    Task<FileListResponse> SearchFilesAsync(string directoryPath, string searchTerm);
    Task<FileListResponse> SearchFilesAsync(string directoryPath, string searchTerm, int maxResults);
    Task<FileListResponse> SearchFilesAsync(string directoryPath, string searchTerm, int maxResults, bool includeSubdirectories);
    Task<byte[]> DownloadFileAsync(string filePath);
    Task<bool> UploadFileAsync(string directoryPath, string fileName, byte[] content);
    Task<bool> CopyFileAsync(string sourcePath, string destinationPath);
    Task<bool> MoveFileAsync(string sourcePath, string destinationPath);
}