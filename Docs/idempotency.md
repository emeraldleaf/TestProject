# Idempotency in File Browser API Service

## Overview

Idempotency is a critical design principle ensuring that performing the same operation multiple times produces the same result as performing it once. In our file browser application, idempotency prevents duplicate operations, race conditions, and provides a better user experience.

## What is Idempotency?

An operation is **idempotent** if calling it multiple times with the same parameters has the same effect as calling it once. This is especially important for:

- **Network operations** that might be retried
- **User interface actions** where users might click buttons multiple times
- **System recovery** scenarios where operations need to be safely repeated

### Examples:
- ✅ **Idempotent**: `GET /api/files/list` - Always returns the same file list
- ✅ **Idempotent**: `PUT /api/files/upload` - Uploading the same file multiple times should not create duplicates
- ❌ **Not Idempotent**: `POST /api/files/createUniqueFile` - Each call creates a new file

## Implementation Architecture

### Defense-in-Depth Approach

Our idempotency implementation uses a **three-layer defense strategy**:

1. **Client-Side**: Request deduplication and UI protection
2. **Server-Side**: Idempotency key validation and result caching
3. **Database**: Constraints and stored procedures for data integrity

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Client Side   │    │   Server Side   │    │    Database     │
│                 │    │                 │    │                 │
│ Request         │    │ Idempotency     │    │ Unique          │
│ Deduplication   │────▶ Key Checking    │────▶ Constraints     │
│                 │    │                 │    │                 │
│ UI Protection   │    │ Result Caching  │    │ Stored          │
│ Debounced Cache │    │ Header Support  │    │ Procedures      │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

## Client-Side Implementation

### 1. File Operations Idempotency (TypeScript)

#### Upload Files
```typescript
async uploadFile(directoryPath: string, file: File, idempotencyKey?: string): Promise<any>
```

**Idempotency Key Strategy:**
- Combines: `directoryPath`, `file.name`, `file.size`, `file.lastModified`
- Ensures same file to same location only uploads once
- Prevents accidental duplicate uploads from rapid clicking

**Example:**
```typescript
// These calls will be deduplicated automatically
await fileService.uploadFile('/documents', myFile);
await fileService.uploadFile('/documents', myFile); // Won't upload again
```

#### Copy/Move Files
```typescript
async copyFile(sourcePath: string, destinationPath: string, idempotencyKey?: string): Promise<any>
async moveFile(sourcePath: string, destinationPath: string, idempotencyKey?: string): Promise<any>
```

**Idempotency Key Strategy:**
- Based on source and destination paths
- Prevents duplicate copy/move operations
- Maintains file system consistency

### 2. Directory Operations Idempotency

#### Directory Refresh
```typescript
async refreshDirectory(directoryPath: string | null, idempotencyKey?: string): Promise<FileListResponse>
```

**Benefits:**
- Prevents multiple concurrent refresh operations
- Reduces server load from rapid refresh requests
- Maintains cache consistency

#### Directory Preload
```typescript
async preloadDirectory(directoryPath: string | null, idempotencyKey?: string): Promise<void>
```

**Benefits:**
- Prevents duplicate preload requests
- Optimizes navigation performance
- Manages cache configuration safely

### 3. Request Deduplication System

The core mechanism uses the existing `withRequestDeduplication` method:

```typescript
async withRequestDeduplication<T>(key: string, requestFn: () => Promise<T>): Promise<T>
```

**How it works:**
1. **Check existing requests**: If a request with the same key is already in progress, return that promise
2. **Execute once**: Only one request per unique key executes at a time
3. **Clean up**: Remove completed requests from the pending map

### 4. Debounced Cache Invalidation

```typescript
invalidateCache(pattern: string): void
```

**Features:**
- **Debouncing**: Groups multiple invalidation calls within 100ms
- **Deduplication**: Prevents duplicate invalidations for the same pattern
- **Performance**: Reduces cache churn and improves responsiveness

## Server-Side Implementation (C#)

### 1. Idempotency Service

```csharp
public interface IIdempotencyService
{
    Task<IActionResult?> GetCachedResultAsync(string idempotencyKey);
    Task StoreCachedResultAsync(string idempotencyKey, IActionResult result, TimeSpan? expiry = null);
    string GenerateKey(string operation, params object[] parameters);
    bool IsValidKey(string? key);
}
```

**Features:**
- **In-memory caching**: Uses IMemoryCache for fast result retrieval
- **Configurable expiry**: Different cache times for success/failure
- **Thread-safe**: Safe for concurrent operations
- **Result serialization**: Handles complex IActionResult types

### 2. Controller Integration

```csharp
[HttpPost("upload")]
public async Task<IActionResult> UploadFile(
    [FromForm] IFormFile file,
    [FromQuery] string path,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
{
    // Check for cached result
    if (!string.IsNullOrEmpty(idempotencyKey))
    {
        var cachedResult = await _idempotencyService.GetCachedResultAsync(idempotencyKey);
        if (cachedResult != null)
        {
            return cachedResult; // Return cached result immediately
        }
    }

    // Perform operation...
    var result = await PerformOperation();

    // Cache the result
    if (!string.IsNullOrEmpty(idempotencyKey))
    {
        await _idempotencyService.StoreCachedResultAsync(idempotencyKey, result);
    }

    return result;
}
```

### 3. HTTP Header Support

**Standard Idempotency Header:**
```http
POST /api/files/upload
Idempotency-Key: user123-upload-2024-01-15-abc123
Content-Type: multipart/form-data
```

**Response Headers:**
```http
HTTP/1.1 200 OK
Cache-Control: private, no-cache
X-Idempotency-Used: true
```

### 4. Caching Strategy

**Cache Expiration:**
- **Successful operations**: 1 hour (long-lived)
- **Failed operations**: 5 minutes (short-lived, allows retries)
- **Cleanup**: Automatic memory management

**Cache Key Validation:**
- Length: 8-128 characters
- Characters: Alphanumeric plus `-`, `_`, `+`, `/`, `=`
- Uniqueness: Must be unique across all operations

## Database Implementation (SQL Server)

### 1. Idempotency Table Structure

```sql
CREATE TABLE FileOperations (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    IdempotencyKey NVARCHAR(128) NOT NULL,
    OperationType NVARCHAR(50) NOT NULL,
    SourcePath NVARCHAR(1000) NULL,
    DestinationPath NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status NVARCHAR(20) NOT NULL DEFAULT 'success',
    ResultData NVARCHAR(MAX) NULL,

    CONSTRAINT UQ_FileOperations_IdempotencyKey UNIQUE (IdempotencyKey)
);
```

### 2. Business Constraints

```sql
-- Prevent duplicate files at same location
CREATE TABLE FileUploads (
    FilePath NVARCHAR(1000) NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    FileHash NVARCHAR(64) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT UQ_FileUploads_Path_Name UNIQUE (FilePath, FileName)
);
```

### 3. Idempotent Stored Procedures

```sql
CREATE PROCEDURE UploadFileIdempotent
    @IdempotencyKey NVARCHAR(128),
    @FilePath NVARCHAR(1000),
    @FileName NVARCHAR(255),
    @ResultData NVARCHAR(MAX) = NULL OUTPUT,
    @IsNewOperation BIT = 0 OUTPUT
AS
BEGIN
    -- Check if operation already exists
    SELECT @ResultData = ResultData
    FROM FileOperations
    WHERE IdempotencyKey = @IdempotencyKey;

    IF @ResultData IS NOT NULL
    BEGIN
        SET @IsNewOperation = 0; -- Already performed
        RETURN;
    END

    -- Perform operation with duplicate checking...
    -- Record result in FileOperations table
END;
```

### 4. Database Benefits

**Durability:**
- Survives server restarts and crashes
- Shared across multiple application instances
- Permanent audit trail of operations

**Consistency:**
- ACID transactions ensure data integrity
- Unique constraints prevent duplicates at database level
- Foreign key relationships maintain referential integrity

**Performance:**
- Indexed for fast idempotency key lookups
- Bulk operations with set-based queries
- Automatic cleanup with scheduled jobs

## Usage Examples

### Client-Side Usage

**Automatic Idempotency (TypeScript):**
```typescript
const fileService = new FileApiService();

// These are automatically idempotent
await fileService.uploadFile('/docs', file);
await fileService.copyFile('/source.txt', '/dest.txt');
await fileService.refreshDirectory('/docs');
```

**Custom Idempotency Keys:**
```typescript
// For custom business logic
await fileService.uploadFile('/docs', file, 'user-action-123');
await fileService.refreshDirectory('/docs', 'manual-refresh-456');
```

### Server-Side Usage

**With HTTP Headers:**
```javascript
// Client sends idempotency key in header
fetch('/api/files/upload', {
    method: 'POST',
    headers: {
        'Idempotency-Key': 'user123-upload-20240115-abc123'
    },
    body: formData
});
```

**Server Processing:**
```csharp
// Controller automatically handles idempotency
[HttpPost("upload")]
public async Task<IActionResult> UploadFile(
    [FromForm] IFormFile file,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
{
    // Automatic caching and duplicate detection
    // Returns cached result if operation already performed
}
```

### Database Integration

**Stored Procedure Usage:**
```sql
DECLARE @ResultData NVARCHAR(MAX);
DECLARE @IsNewOperation BIT;

EXEC UploadFileIdempotent
    @IdempotencyKey = 'user123-upload-20240115',
    @FilePath = '/documents',
    @FileName = 'report.pdf',
    @ResultData = @ResultData OUTPUT,
    @IsNewOperation = @IsNewOperation OUTPUT;

-- @IsNewOperation = 1 if this was a new operation
-- @IsNewOperation = 0 if operation was already performed
```

### User Interface Integration
```typescript
// Prevent double-clicks from causing issues
async function handleUploadClick() {
    const button = document.getElementById('upload-btn');
    button.disabled = true;

    try {
        // Idempotency ensures safety even if called multiple times
        await fileService.uploadFile(currentPath, selectedFile);
    } finally {
        button.disabled = false;
    }
}
```

## Benefits

### 1. **User Experience**
- No accidental duplicate operations from rapid clicking
- Consistent behavior regardless of network conditions
- Predictable file system state

### 2. **Performance**
- Reduced server load from duplicate requests
- Efficient cache management
- Optimized network utilization

### 3. **Reliability**
- Safe operation retries during network issues
- Consistent state management
- Race condition prevention

### 4. **Developer Experience**
- Simplified error handling
- Predictable API behavior
- Easier testing and debugging

## Best Practices

### 1. **Let the System Handle It**
```typescript
// ✅ Good - Use default idempotency
await fileService.uploadFile(path, file);

// ❌ Avoid - Manual duplicate prevention
if (!isUploading) {
    isUploading = true;
    await fileService.uploadFile(path, file);
    isUploading = false;
}
```

### 2. **Custom Keys for Business Logic**
```typescript
// ✅ Good - Custom key for specific business needs
await fileService.copyFile(source, dest, `batch-operation-${batchId}`);

// ❌ Avoid - Random keys that break idempotency
await fileService.copyFile(source, dest, Math.random().toString());
```

### 3. **Handle Errors Gracefully**
```typescript
// ✅ Good - Idempotent operations are safe to retry
try {
    await fileService.uploadFile(path, file);
} catch (error) {
    // Safe to retry - idempotency prevents duplicates
    await fileService.uploadFile(path, file);
}
```

## Monitoring and Debugging

### Cache Statistics
```typescript
const stats = fileService.getCacheStats();
console.log('Pending requests:', stats.pending);
```

### Performance Metrics
```typescript
const metrics = fileService.getPerformanceMetrics();
console.log('Cache hit rate:', metrics.hitRate);
```

## Technical Details

### Idempotency Key Generation
- **Deterministic**: Same inputs always generate the same key
- **Unique**: Different operations generate different keys
- **Collision-resistant**: Low probability of accidental key conflicts

### Memory Management
- **Automatic cleanup**: Completed requests are removed from memory
- **Timeout handling**: Stale requests are eventually cleaned up
- **Bounded memory**: Cache size limits prevent memory leaks

### Error Handling
- **Transparent retries**: Failed operations can be safely retried
- **State consistency**: Errors don't leave the system in inconsistent states
- **Graceful degradation**: System continues working even with partial failures

## Summary

| Layer | Implementation | Coverage | Benefits |
|-------|---------------|----------|----------|
| **Client** | TypeScript/JavaScript | UI interactions, rapid requests | Performance, UX |
| **Server** | C# Controllers + Service | HTTP requests, caching | Reliability, multi-instance |
| **Database** | SQL constraints + procedures | Data integrity, audit | Durability, consistency |

## Conclusion

Our comprehensive idempotency implementation provides **defense-in-depth protection** across all layers of the application:

### ✅ **Complete Coverage**
- **Client-side**: Prevents unnecessary requests and improves performance
- **Server-side**: Handles network issues, retries, and multi-instance scenarios
- **Database**: Ensures data integrity and provides permanent audit trail

### ✅ **Production Ready**
- **Scalable**: Works across multiple server instances
- **Durable**: Survives server restarts and crashes
- **Performant**: Efficient caching and debouncing strategies
- **Secure**: Proper validation and sanitization

### ✅ **Developer Friendly**
- **Automatic**: Works out-of-the-box with sensible defaults
- **Flexible**: Supports custom idempotency keys for specific needs
- **Observable**: Comprehensive logging and monitoring capabilities
- **Maintainable**: Clean separation of concerns and clear interfaces

The implementation balances simplicity with robustness, providing automatic protection for common scenarios while enabling sophisticated customization for complex business requirements. This ensures reliable file operations regardless of network conditions, user behavior, or system failures.