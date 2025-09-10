namespace TestProject.Models;

public record FileItem(
    string Name,
    string Path,
    long Size,
    DateTime LastModified,
    bool IsDirectory
);

public record FileListResponse(
    IEnumerable<FileItem> Files,
    string DirectoryPath,
    bool Success,
    string? ErrorMessage = null
);