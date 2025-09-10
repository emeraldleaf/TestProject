namespace TestProject.Models;

// Use records for immutable data with value semantics instead of classes with reference semantics
public record FileItem(
    string Name,
    string Path,
    long Size,
    DateTime LastModified,
    bool IsDirectory
);

// Response model for file listing operations
public record FileListResponse(
    IEnumerable<FileItem> Files,
    string DirectoryPath,
    bool Success,
    string? ErrorMessage = null
);