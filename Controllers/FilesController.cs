using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TestProject.Services;
using TestProject.Security;

namespace TestProject.Controllers;

// REST API controller for secure file system operations
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly ISecurityValidationService _securityService;
    private readonly ILogger<FilesController> _logger;
    private readonly SecurityOptions _securityOptions;

    public FilesController(
        IFileService fileService, 
        ISecurityValidationService securityService,
        ILogger<FilesController> logger,
        IOptions<SecurityOptions> securityOptions)
    {
        _fileService = fileService;
        _securityService = securityService;
        _logger = logger;
        _securityOptions = securityOptions.Value;
    }

    // Get files and directories for the specified path
    [HttpGet]
    public async Task<IActionResult> GetFiles([FromQuery] string? path = null)
    {
        try
        {
            // Validate and sanitize the input path
            var pathValidation = _securityService.ValidateAndSanitizePath(path);
            if (!pathValidation.IsValid)
            {
                _logger.LogWarning("Invalid path provided: {Path}, Error: {Error}", path, pathValidation.ErrorMessage);
                return BadRequest(new { Success = false, ErrorMessage = pathValidation.ErrorMessage });
            }
            
            var directoryPath = pathValidation.SanitizedPath!;
            _logger.LogInformation("Getting files for directory: {DirectoryPath}", directoryPath);
            
            var response = await _fileService.GetFilesAsync(directoryPath);
            
            if (!response.Success)
            {
                _logger.LogWarning("Failed to get files: {ErrorMessage}", response.ErrorMessage);
                return BadRequest(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting files");
            return StatusCode(500, new { Success = false, ErrorMessage = "Internal server error" });
        }
    }

    // Return the configured default/base path
    [HttpGet("defaultpath")]
    public IActionResult GetDefaultPath()
    {
        try
        {
            return Ok(new { defaultPath = _securityOptions.AllowedBasePath });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting default path");
            return StatusCode(500, new { Success = false, ErrorMessage = "Internal server error" });
        }
    }

    // Search for files matching the given term
    [HttpGet("search")]
    public async Task<IActionResult> SearchFiles([FromQuery] string? path, [FromQuery] string? term, [FromQuery] bool includeSubdirectories = true)
    {
        // Validate search term
        var termValidation = _securityService.ValidateSearchTerm(term);
        if (!termValidation.IsValid)
        {
            _logger.LogWarning("Invalid search term: {Term}, Error: {Error}", term, termValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = termValidation.ErrorMessage });
        }

        // Validate path
        var pathValidation = _securityService.ValidateAndSanitizePath(path);
        if (!pathValidation.IsValid)
        {
            _logger.LogWarning("Invalid search path: {Path}, Error: {Error}", path, pathValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = pathValidation.ErrorMessage });
        }

        try
        {
            var directoryPath = pathValidation.SanitizedPath!;
            var sanitizedTerm = termValidation.SanitizedTerm!;
            
            _logger.LogInformation("Searching files in {Path} for term: {Term}, IncludeSubdirs: {IncludeSubdirs}", directoryPath, sanitizedTerm, includeSubdirectories);
            
            var response = await _fileService.SearchFilesAsync(directoryPath, sanitizedTerm, 10000, includeSubdirectories);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files");
            return StatusCode(500, new { Success = false, ErrorMessage = "Internal server error" });
        }
    }

    // Download a file as binary stream
    [HttpGet("download")]
    public async Task<IActionResult> DownloadFile([FromQuery] string? path)
    {
        // Validate and sanitize the file path
        var pathValidation = _securityService.ValidateAndSanitizePath(path);
        if (!pathValidation.IsValid)
        {
            _logger.LogWarning("Invalid download path: {Path}, Error: {Error}", path, pathValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = pathValidation.ErrorMessage });
        }

        try
        {
            var filePath = pathValidation.SanitizedPath!;
            _logger.LogInformation("Downloading file: {FilePath}", filePath);
            
            var content = await _fileService.DownloadFileAsync(filePath);
            var fileName = Path.GetFileName(filePath);
            
            return File(content, "application/octet-stream", fileName);
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("File not found for download: {Path}", path);
            return NotFound(new { Success = false, ErrorMessage = "File not found" });
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Unauthorized access attempt for file: {Path}", path);
            return Forbid("Access denied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {Path}", path);
            return StatusCode(500, new { Success = false, ErrorMessage = "Internal server error" });
        }
    }

    // Upload a file to the specified directory
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile? file, [FromQuery] string? path)
    {
        // Validate file upload
        var fileValidation = _securityService.ValidateFileUpload(file!, file?.FileName ?? "");
        if (!fileValidation.IsValid)
        {
            _logger.LogWarning("Invalid file upload: {Error}", fileValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = fileValidation.ErrorMessage });
        }

        // Validate destination path
        var pathValidation = _securityService.ValidateAndSanitizePath(path);
        if (!pathValidation.IsValid)
        {
            _logger.LogWarning("Invalid upload path: {Path}, Error: {Error}", path, pathValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = pathValidation.ErrorMessage });
        }

        try
        {
            var directoryPath = pathValidation.SanitizedPath!;
            var sanitizedFileName = fileValidation.SanitizedFileName!;
            
            _logger.LogInformation("Uploading file {FileName} to {Path}, Size: {Size} bytes", 
                sanitizedFileName, directoryPath, fileValidation.FileSize);
            
            using var stream = new MemoryStream();
            await file!.CopyToAsync(stream);
            var content = stream.ToArray();
            
            var success = await _fileService.UploadFileAsync(directoryPath, sanitizedFileName, content);
            
            if (success)
            {
                return Ok(new { 
                    Success = true, 
                    FileName = sanitizedFileName,
                    Size = fileValidation.FileSize,
                    Message = "File uploaded successfully" 
                });
            }
            else
            {
                return BadRequest(new { Success = false, ErrorMessage = "Upload failed" });
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Unauthorized upload attempt to path: {Path}", path);
            return Forbid("Access denied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file");
            return StatusCode(500, new { Success = false, ErrorMessage = "Internal server error" });
        }
    }

    // Copy a file from source to destination
    [HttpPost("copy")]
    public async Task<IActionResult> CopyFile([FromQuery] string? sourcePath, [FromQuery] string? destinationPath)
    {
        // Validate source path
        var sourceValidation = _securityService.ValidateAndSanitizePath(sourcePath);
        if (!sourceValidation.IsValid)
        {
            _logger.LogWarning("Invalid source path for copy: {Path}, Error: {Error}", sourcePath, sourceValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = $"Source path error: {sourceValidation.ErrorMessage}" });
        }

        // Validate destination path
        var destValidation = _securityService.ValidateAndSanitizePath(destinationPath);
        if (!destValidation.IsValid)
        {
            _logger.LogWarning("Invalid destination path for copy: {Path}, Error: {Error}", destinationPath, destValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = $"Destination path error: {destValidation.ErrorMessage}" });
        }

        try
        {
            var source = sourceValidation.SanitizedPath!;
            var destination = destValidation.SanitizedPath!;
            
            _logger.LogInformation("Copying file from {Source} to {Destination}", source, destination);
            
            var success = await _fileService.CopyFileAsync(source, destination);
            return success 
                ? Ok(new { Success = true, Message = "File copied successfully" })
                : BadRequest(new { Success = false, ErrorMessage = "Copy failed" });
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Unauthorized copy attempt: {Source} to {Destination}", sourcePath, destinationPath);
            return Forbid("Access denied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying file");
            return StatusCode(500, new { Success = false, ErrorMessage = "Internal server error" });
        }
    }

    // Move a file from source to destination
    [HttpPost("move")]
    public async Task<IActionResult> MoveFile([FromQuery] string? sourcePath, [FromQuery] string? destinationPath)
    {
        // Validate source path
        var sourceValidation = _securityService.ValidateAndSanitizePath(sourcePath);
        if (!sourceValidation.IsValid)
        {
            _logger.LogWarning("Invalid source path for move: {Path}, Error: {Error}", sourcePath, sourceValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = $"Source path error: {sourceValidation.ErrorMessage}" });
        }

        // Validate destination path
        var destValidation = _securityService.ValidateAndSanitizePath(destinationPath);
        if (!destValidation.IsValid)
        {
            _logger.LogWarning("Invalid destination path for move: {Path}, Error: {Error}", destinationPath, destValidation.ErrorMessage);
            return BadRequest(new { Success = false, ErrorMessage = $"Destination path error: {destValidation.ErrorMessage}" });
        }

        try
        {
            var source = sourceValidation.SanitizedPath!;
            var destination = destValidation.SanitizedPath!;
            
            _logger.LogInformation("Moving file from {Source} to {Destination}", source, destination);
            
            var success = await _fileService.MoveFileAsync(source, destination);
            return success 
                ? Ok(new { Success = true, Message = "File moved successfully" })
                : BadRequest(new { Success = false, ErrorMessage = "Move failed" });
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Unauthorized move attempt: {Source} to {Destination}", sourcePath, destinationPath);
            return Forbid("Access denied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving file");
            return StatusCode(500, new { Success = false, ErrorMessage = "Internal server error" });
        }
    }
}