using TestProject.Models;

namespace TestProject.Services;

public class FileSystemService : IFileService
{
    public async Task<FileListResponse> GetFilesAsync(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return new FileListResponse(
                    [],
                    directoryPath,
                    false,
                    $"Directory '{directoryPath}' does not exist"
                );
            }

            // Get all files and directories in the path
            // var allPaths = Directory.GetFileSystemEntries(directoryPath);
            
            // Convert each path to a FileItem with metadata
            // var fileItems = new List<FileItem>();
            // foreach (var path in allPaths)
            // {
            //     var fileItem = CreateFileItem(path);
            //     fileItems.Add(fileItem);
            // }

            // More efficient approach using DirectoryInfo
            var directoryInfo = new DirectoryInfo(directoryPath);
            var files = directoryInfo.GetFileSystemInfos()
                .Select(info => new FileItem(
                    Name: info.Name,
                    Path: info.FullName,
                    Size: info is FileInfo fileInfo ? fileInfo.Length : 0,
                    LastModified: info.LastWriteTime,
                    IsDirectory: info is DirectoryInfo
                ))
                .OrderBy(item => item.IsDirectory ? 0 : 1)  // Directories (0) come before files (1)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)  // Then sort by name A-Z
                .ToList();
            
            await Task.CompletedTask; // Satisfy async method signature

            return new FileListResponse(files, directoryPath, true);
        }
        catch (UnauthorizedAccessException)
        {
            return new FileListResponse(
                Enumerable.Empty<FileItem>(),
                directoryPath,
                false,
                "Access denied to directory"
            );
        }
        catch (Exception ex)
        {
            return new FileListResponse(
                Enumerable.Empty<FileItem>(),
                directoryPath,
                false,
                $"Error reading directory: {ex.Message}"
            );
        }
    }

    private static FileItem CreateFileItem(string path)
    {
        var info = new FileInfo(path);
        bool isDirectory = Directory.Exists(path);

        return new FileItem(
            Name: Path.GetFileName(path),
            Path: path,
            Size: isDirectory ? 0 : info.Length,
            LastModified: info.LastWriteTime,
            IsDirectory: isDirectory
        );
    }

    private static int GetDirectoryDepth(string filePath, string rootPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(rootPath, filePath);
            if (relativePath == "." || relativePath == Path.GetFileName(filePath))
            {
                return 0; // File/directory is in the root directory
            }
            
            // Count the directory separators to determine depth
            return relativePath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return int.MaxValue; // If we can't determine depth, put it at the end
        }
    }

    private static FileItem CreateSearchFileItem(string path, string searchRoot)
    {
        var info = new FileInfo(path);
        bool isDirectory = Directory.Exists(path);
        
        // Show relative path from search root for better context in search results
        var relativePath = Path.GetRelativePath(searchRoot, path);
        var displayName = relativePath == Path.GetFileName(path) 
            ? Path.GetFileName(path)  // If in root directory, just show filename
            : relativePath;           // If in subdirectory, show relative path

        return new FileItem(
            Name: displayName,
            Path: path,
            Size: isDirectory ? 0 : info.Length,
            LastModified: info.LastWriteTime,
            IsDirectory: isDirectory
        );
    }

    private static FileItem CreateSearchFileItemOptimized(string path, string searchRoot, bool isDirectory)
    {
        // Show relative path from search root for better context in search results
        var relativePath = Path.GetRelativePath(searchRoot, path);
        var displayName = relativePath == Path.GetFileName(path) 
            ? Path.GetFileName(path)  // If in root directory, just show filename
            : relativePath;           // If in subdirectory, show relative path

        if (isDirectory)
        {
            return new FileItem(
                Name: displayName,
                Path: path,
                Size: 0,
                LastModified: Directory.GetLastWriteTime(path),
                IsDirectory: true
            );
        }
        else
        {
            var info = new FileInfo(path);
            return new FileItem(
                Name: displayName,
                Path: path,
                Size: info.Length,
                LastModified: info.LastWriteTime,
                IsDirectory: false
            );
        }
    }

    public async Task<FileListResponse> SearchFilesAsync(string directoryPath, string searchTerm)
    {
        return await SearchFilesAsync(directoryPath, searchTerm, 10000, true);
    }

    public async Task<FileListResponse> SearchFilesAsync(string directoryPath, string searchTerm, int maxResults)
    {
        return await SearchFilesAsync(directoryPath, searchTerm, maxResults, true);
    }

    public async Task<FileListResponse> SearchFilesAsync(string directoryPath, string searchTerm, int maxResults, bool includeSubdirectories)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return new FileListResponse(
                    [],
                    directoryPath,
                    false,
                    $"Directory '{directoryPath}' does not exist"
                );
            }

            var (results, wasTruncated) = await Task.Run(() => 
            {
                var results = new List<FileItem>();
                bool truncated = false;
                var searchPattern = $"*{searchTerm}*";
                int skippedDirectories = 0;
                
                // Search that respects includeSubdirectories setting
                var startTime = DateTime.UtcNow;
                bool timedOut = false;
                
                void SearchRecursive(string currentDir, int depth = 0)
                {
                    if (results.Count >= maxResults + 1) return; // Stop early if we've hit our limit
                    if (!includeSubdirectories && depth > 0) return; // Skip subdirectories if not included
                    if (depth > 20) return; // Reduce max depth for performance
                    if (DateTime.UtcNow - startTime > TimeSpan.FromSeconds(30)) 
                    {
                        timedOut = true;
                        return; // 30 second timeout
                    }
                    
                    try
                    {
                        // Search files in current directory
                        foreach (var file in Directory.EnumerateFiles(currentDir, searchPattern))
                        {
                            if (results.Count >= maxResults + 1) break;
                            results.Add(CreateSearchFileItemOptimized(file, directoryPath, false));
                        }
                        
                        // Search directories in current directory
                        foreach (var dir in Directory.EnumerateDirectories(currentDir, searchPattern))
                        {
                            if (results.Count >= maxResults + 1) break;
                            results.Add(CreateSearchFileItemOptimized(dir, directoryPath, true));
                        }
                        
                        // Recursively search subdirectories only if includeSubdirectories is true
                        if (includeSubdirectories && results.Count < maxResults + 1)
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                            {
                                if (results.Count >= maxResults + 1) break;
                                SearchRecursive(subDir, depth + 1);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        skippedDirectories++;
                        // Skip this directory and continue with others
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Directory might have been deleted, skip it
                    }
                    catch (IOException)
                    {
                        // Handle other IO errors gracefully
                        skippedDirectories++;
                    }
                }
                
                // Start the recursive search
                SearchRecursive(directoryPath);
                
                // Add debug info about skipped directories and timeout
                if (timedOut)
                {
                    results.Add(new FileItem($"DEBUG: Search timed out after 30 seconds, skipped {skippedDirectories} directories", directoryPath, 0, DateTime.Now, false));
                }
                else if (skippedDirectories > 0)
                {
                    results.Add(new FileItem($"DEBUG: Skipped {skippedDirectories} directories due to permissions", directoryPath, 0, DateTime.Now, false));
                }
                
                // Check for truncation
                if (results.Where(r => !r.Name.StartsWith("DEBUG:")).Count() > maxResults)
                {
                    truncated = true;
                    
                    // Separate debug items from real results
                    var debugItems = results.Where(r => r.Name.StartsWith("DEBUG:")).ToList();
                    var realResults = results.Where(r => !r.Name.StartsWith("DEBUG:")).Take(maxResults).ToList();
                    results = realResults.Concat(debugItems).ToList();
                    
                }
                
                var finalResults = results
                    .OrderBy(f => f.Name.StartsWith("DEBUG:") ? 1 : 0) // Debug items last
                    .ThenBy(f => !f.IsDirectory) // Directories first
                    .ThenBy(f => GetDirectoryDepth(f.Path, directoryPath)) // Sort by depth (shallower first)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase) // Then by name
                    .ToList();
                
                return (finalResults, truncated);
            });

            string? message = null;
            var realResultCount = results.Where(r => !r.Name.StartsWith("DEBUG:")).Count();
            
            if (wasTruncated)
            {
                message = $"Search returned {maxResults}+ results. Try a more specific search term.";
            }
            else if (realResultCount > 0)
            {
                message = $"Found {realResultCount} results (limit: {maxResults})";
                if (results.Any(r => r.Name.StartsWith("DEBUG:")))
                {
                    message += $" - {results.First(r => r.Name.StartsWith("DEBUG:")).Name.Replace("DEBUG: ", "")}";
                }
            }
            else
            {
                message = $"No results found for '{searchTerm}' in '{directoryPath}' (limit: {maxResults})";
                if (results.Any(r => r.Name.StartsWith("DEBUG:")))
                {
                    message += $" - {results.First(r => r.Name.StartsWith("DEBUG:")).Name.Replace("DEBUG: ", "")}";
                }
            }

            return new FileListResponse(results, directoryPath, true, message);
        }
        catch (Exception ex)
        {
            return new FileListResponse(
                Enumerable.Empty<FileItem>(),
                directoryPath,
                false,
                $"Error searching files: {ex.Message}"
            );
        }
    }

    public async Task<byte[]> DownloadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File '{filePath}' not found");
        
        return await File.ReadAllBytesAsync(filePath);
    }

    public async Task<bool> UploadFileAsync(string directoryPath, string fileName, byte[] content)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return false;
            
            var filePath = Path.Combine(directoryPath, fileName);
            await File.WriteAllBytesAsync(filePath, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CopyFileAsync(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return false;
                
            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                Directory.CreateDirectory(destinationDir);
                
            await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite: true));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> MoveFileAsync(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return false;

            // If destinationPath is a directory, append the source file name
            if (Directory.Exists(destinationPath))
            {
                var fileName = Path.GetFileName(sourcePath);
                destinationPath = Path.Combine(destinationPath, fileName);
            }
                
            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                Directory.CreateDirectory(destinationDir);
                
            await Task.Run(() => File.Move(sourcePath, destinationPath, overwrite: true));
            return true;
        }
        catch
        {
            return false;
        }
    }
}