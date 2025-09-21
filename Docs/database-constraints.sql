-- Database Constraints for File Operations Idempotency
-- This example shows how to implement database-level constraints to prevent duplicate operations

-- Create a table to track file operations with idempotency
CREATE TABLE FileOperations (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    IdempotencyKey NVARCHAR(128) NOT NULL,
    OperationType NVARCHAR(50) NOT NULL, -- 'upload', 'copy', 'move'
    SourcePath NVARCHAR(1000) NULL,      -- NULL for upload operations
    DestinationPath NVARCHAR(1000) NOT NULL,
    FileSize BIGINT NULL,
    FileName NVARCHAR(255) NULL,
    UserId NVARCHAR(100) NULL,           -- Optional: track which user performed operation
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status NVARCHAR(20) NOT NULL DEFAULT 'success', -- 'success', 'failed', 'in_progress'
    ResultData NVARCHAR(MAX) NULL,       -- JSON result data for caching

    -- Idempotency constraint: prevent duplicate operations with same key
    CONSTRAINT UQ_FileOperations_IdempotencyKey UNIQUE (IdempotencyKey),

    -- Ensure operation type is valid
    CONSTRAINT CK_FileOperations_OperationType
        CHECK (OperationType IN ('upload', 'copy', 'move')),

    -- Ensure status is valid
    CONSTRAINT CK_FileOperations_Status
        CHECK (Status IN ('success', 'failed', 'in_progress')),

    -- Business rule: copy and move operations must have source path
    CONSTRAINT CK_FileOperations_SourcePath
        CHECK (
            (OperationType = 'upload' AND SourcePath IS NULL) OR
            (OperationType IN ('copy', 'move') AND SourcePath IS NOT NULL)
        )
);

-- Index for fast lookup by idempotency key
CREATE INDEX IX_FileOperations_IdempotencyKey
ON FileOperations (IdempotencyKey);

-- Index for cleanup queries (finding old operations)
CREATE INDEX IX_FileOperations_CreatedAt
ON FileOperations (CreatedAt);

-- Index for user-specific queries
CREATE INDEX IX_FileOperations_UserId_CreatedAt
ON FileOperations (UserId, CreatedAt)
WHERE UserId IS NOT NULL;

-- Prevent duplicate file uploads to same path (additional business constraint)
CREATE TABLE FileUploads (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    FilePath NVARCHAR(1000) NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    FileSize BIGINT NOT NULL,
    FileHash NVARCHAR(64) NULL,         -- SHA-256 hash for content-based deduplication
    UserId NVARCHAR(100) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastModified DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    -- Prevent duplicate files at same location
    CONSTRAINT UQ_FileUploads_Path_Name UNIQUE (FilePath, FileName),

    -- Optional: Prevent duplicate content (same hash)
    -- CONSTRAINT UQ_FileUploads_Hash UNIQUE (FileHash) WHERE FileHash IS NOT NULL
);

-- Index for fast file lookups
CREATE INDEX IX_FileUploads_FilePath_FileName
ON FileUploads (FilePath, FileName);

-- Index for content-based deduplication
CREATE INDEX IX_FileUploads_FileHash
ON FileUploads (FileHash)
WHERE FileHash IS NOT NULL;

-- Create a cleanup procedure for old idempotency records
CREATE PROCEDURE CleanupIdempotencyRecords
    @RetentionDays INT = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, GETUTCDATE());

    -- Delete old completed operations (keep failed ones longer for debugging)
    DELETE FROM FileOperations
    WHERE CreatedAt < @CutoffDate
    AND Status = 'success';

    -- Clean up very old failed operations (30 days)
    DELETE FROM FileOperations
    WHERE CreatedAt < DATEADD(DAY, -30, GETUTCDATE())
    AND Status = 'failed';

    SELECT @@ROWCOUNT as RecordsDeleted;
END;

-- Example stored procedure for idempotent file upload
CREATE PROCEDURE UploadFileIdempotent
    @IdempotencyKey NVARCHAR(128),
    @FilePath NVARCHAR(1000),
    @FileName NVARCHAR(255),
    @FileSize BIGINT,
    @FileHash NVARCHAR(64) = NULL,
    @UserId NVARCHAR(100) = NULL,
    @ResultData NVARCHAR(MAX) = NULL OUTPUT,
    @IsNewOperation BIT = 0 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IsNewOperation = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Check if operation already exists
        SELECT @ResultData = ResultData
        FROM FileOperations
        WHERE IdempotencyKey = @IdempotencyKey;

        IF @ResultData IS NOT NULL
        BEGIN
            -- Operation already performed, return cached result
            SET @IsNewOperation = 0;
            COMMIT TRANSACTION;
            RETURN;
        END

        -- Check for duplicate file at same location
        IF EXISTS (SELECT 1 FROM FileUploads WHERE FilePath = @FilePath AND FileName = @FileName)
        BEGIN
            -- File already exists at this location
            SET @ResultData = '{"Success": false, "ErrorMessage": "File already exists at this location"}';

            -- Record the operation attempt
            INSERT INTO FileOperations (IdempotencyKey, OperationType, DestinationPath, FileSize, FileName, UserId, Status, ResultData)
            VALUES (@IdempotencyKey, 'upload', @FilePath + '\' + @FileName, @FileSize, @FileName, @UserId, 'failed', @ResultData);

            SET @IsNewOperation = 1;
            COMMIT TRANSACTION;
            RETURN;
        END

        -- Record successful operation
        INSERT INTO FileUploads (FilePath, FileName, FileSize, FileHash, UserId)
        VALUES (@FilePath, @FileName, @FileSize, @FileHash, @UserId);

        SET @ResultData = '{"Success": true, "Message": "File uploaded successfully"}';

        INSERT INTO FileOperations (IdempotencyKey, OperationType, DestinationPath, FileSize, FileName, UserId, Status, ResultData)
        VALUES (@IdempotencyKey, 'upload', @FilePath + '\' + @FileName, @FileSize, @FileName, @UserId, 'success', @ResultData);

        SET @IsNewOperation = 1;
        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;

        -- Record failed operation
        SET @ResultData = '{"Success": false, "ErrorMessage": "' + ERROR_MESSAGE() + '"}';

        INSERT INTO FileOperations (IdempotencyKey, OperationType, DestinationPath, FileSize, FileName, UserId, Status, ResultData)
        VALUES (@IdempotencyKey, 'upload', @FilePath + '\' + @FileName, @FileSize, @FileName, @UserId, 'failed', @ResultData);

        SET @IsNewOperation = 1;
        THROW;
    END CATCH
END;

-- Example usage:
/*
DECLARE @ResultData NVARCHAR(MAX);
DECLARE @IsNewOperation BIT;

EXEC UploadFileIdempotent
    @IdempotencyKey = 'unique-upload-key-123',
    @FilePath = '/documents/reports',
    @FileName = 'report.pdf',
    @FileSize = 1024000,
    @FileHash = 'abc123def456...',
    @UserId = 'user123',
    @ResultData = @ResultData OUTPUT,
    @IsNewOperation = @IsNewOperation OUTPUT;

SELECT @ResultData as Result, @IsNewOperation as IsNew;
*/

-- Schedule cleanup job (example for SQL Server Agent)
/*
-- Create a job to run cleanup daily
EXEC dbo.sp_add_job
    @job_name = 'Cleanup Idempotency Records',
    @description = 'Remove old idempotency records to prevent table bloat';

EXEC dbo.sp_add_jobstep
    @job_name = 'Cleanup Idempotency Records',
    @step_name = 'Run Cleanup',
    @command = 'EXEC CleanupIdempotencyRecords @RetentionDays = 7';

EXEC dbo.sp_add_schedule
    @schedule_name = 'Daily Cleanup',
    @freq_type = 4, -- Daily
    @freq_interval = 1,
    @active_start_time = 020000; -- 2:00 AM

EXEC dbo.sp_attach_schedule
    @job_name = 'Cleanup Idempotency Records',
    @schedule_name = 'Daily Cleanup';
*/