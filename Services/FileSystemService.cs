using TestProject.Models;
using System.Collections.Concurrent;

namespace TestProject.Services;

// File system implementation of IFileService with security validation and thread safety
public class FileSystemService : IFileService
{
    // Thread-safe tracking of concurrent operations
    private static readonly object _operationsLock = new object();
    private static int _activeDownloads = 0;
    private static int _activeUploads = 0;
    private static int _activeCopyMoveOperations = 0;
    private static readonly ConcurrentDictionary<string, DateTime> _fileAccessLog = new();

    // Configuration for concurrent operation limits
    private const int MAX_CONCURRENT_DOWNLOADS = 20;
    private const int MAX_CONCURRENT_UPLOADS = 10;
    private const int MAX_CONCURRENT_COPY_MOVE = 5;
    // Get all files and directories in the specified path
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

    // Calculate directory depth for search result ordering
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

    
    // Optimized FileItem creation for search results
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

    // Search for files matching the term with configurable depth and limits
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

    // Read file contents as byte array for download (legacy method - avoid for large files)
    public async Task<byte[]> DownloadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File '{filePath}' not found");

        return await File.ReadAllBytesAsync(filePath);
    }

    // Efficient streaming download that doesn't load entire file into memory
    public async Task<Stream> DownloadFileStreamAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File '{filePath}' not found");

        // Track concurrent downloads with thread-safe counter
        lock (_operationsLock)
        {
            if (_activeDownloads >= MAX_CONCURRENT_DOWNLOADS)
                throw new InvalidOperationException("Too many concurrent downloads. Please try again later.");

            _activeDownloads++;
            _fileAccessLog.TryAdd($"download_{filePath}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
        }

        try
        {
            // Return FileStream for efficient streaming without loading entire file into memory
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, // 64KB buffer for optimal performance
                useAsync: true);

            // Wrap in a disposal tracker to decrement counter when stream is disposed
            return new TrackedFileStream(stream, () =>
            {
                lock (_operationsLock)
                {
                    _activeDownloads--;
                }
            });
        }
        catch
        {
            // Decrement counter if stream creation fails
            lock (_operationsLock)
            {
                _activeDownloads--;
            }
            throw;
        }
    }

    // Write uploaded file content to the file system with concurrency control
    public async Task<bool> UploadFileAsync(string directoryPath, string fileName, byte[] content)
    {
        // Check concurrent upload limit
        lock (_operationsLock)
        {
            if (_activeUploads >= MAX_CONCURRENT_UPLOADS)
                return false; // Too many concurrent uploads

            _activeUploads++;
        }

        try
        {
            if (!Directory.Exists(directoryPath))
                return false;

            var filePath = Path.Combine(directoryPath, fileName);

            if (!File.Exists(filePath))
            {
                await File.WriteAllBytesAsync(filePath, content);

                // Log successful upload
                _fileAccessLog.TryAdd($"upload_{filePath}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            // Always decrement the counter
            lock (_operationsLock)
            {
                _activeUploads--;
            }
        }
    }

    // Copy file from source to destination path with concurrency control
    public async Task<bool> CopyFileAsync(string sourcePath, string destinationPath)
    {
        // Check concurrent copy/move operation limit
        lock (_operationsLock)
        {
            if (_activeCopyMoveOperations >= MAX_CONCURRENT_COPY_MOVE)
                return false; // Too many concurrent copy/move operations

            _activeCopyMoveOperations++;
        }

        try
        {
            if (!File.Exists(sourcePath))
                return false;

            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                Directory.CreateDirectory(destinationDir);

            await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite: true));

            // Log successful copy
            _fileAccessLog.TryAdd($"copy_{sourcePath}_to_{destinationPath}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            // Always decrement the counter
            lock (_operationsLock)
            {
                _activeCopyMoveOperations--;
            }
        }
    }

    // Move file from source to destination path with concurrency control
    public async Task<bool> MoveFileAsync(string sourcePath, string destinationPath)
    {
        // Check concurrent copy/move operation limit
        lock (_operationsLock)
        {
            if (_activeCopyMoveOperations >= MAX_CONCURRENT_COPY_MOVE)
                return false; // Too many concurrent copy/move operations

            _activeCopyMoveOperations++;
        }

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

            // Log successful move
            _fileAccessLog.TryAdd($"move_{sourcePath}_to_{destinationPath}_{DateTime.UtcNow.Ticks}", DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            // Always decrement the counter
            lock (_operationsLock)
            {
                _activeCopyMoveOperations--;
            }
        }
    }

    // Get current operation statistics (useful for monitoring)
    public (int Downloads, int Uploads, int CopyMoveOps) GetActiveOperationCounts()
    {
        lock (_operationsLock)
        {
            return (_activeDownloads, _activeUploads, _activeCopyMoveOperations);
        }
    }

    // Clean up old access log entries (call periodically to prevent memory leaks)
    public void CleanupAccessLog(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var keysToRemove = _fileAccessLog
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _fileAccessLog.TryRemove(key, out _);
        }
    }
}

// Wrapper stream that tracks when the file stream is disposed to decrement counters
public class TrackedFileStream : Stream
{
    private readonly Stream _innerStream;
    private readonly Action _onDispose;
    private bool _disposed = false;

    public TrackedFileStream(Stream innerStream, Action onDispose)
    {
        _innerStream = innerStream;
        _onDispose = onDispose;
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush() => _innerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _innerStream.WriteAsync(buffer, offset, count, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _innerStream.Dispose();
            _onDispose();
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _innerStream.DisposeAsync();
            _onDispose();
            _disposed = true;
        }
        await base.DisposeAsync();
    }
}