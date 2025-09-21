# Application Flow Guide for Junior Developers

## Overview
This is a web-based file browser application built with ASP.NET Core backend and vanilla JavaScript frontend. The app allows users to browse, upload, download, and manage files through a web interface.

## Architecture Overview

```
Frontend (Browser)
    ↓ HTTP Requests
Security Middleware → Controllers → Services → File System
    ↑ HTTP Responses
Frontend (Browser)
```

## Application Startup Flow

### 1. Application Entry Point (`Program.cs:4-12`)
```
1. WebApplication.CreateBuilder() - Creates the app builder
2. ConfigureApplicationServices() - Sets up dependency injection
3. builder.Build() - Creates the app instance
4. ConfigureSecurePipeline() - Sets up middleware pipeline
5. app.Run() - Starts the web server
```

### 2. Service Configuration (`ProgramExtensions.cs:24-86`)
```
1. Security options configuration
2. Dependency injection registration:
   - ISecurityValidationService (Singleton)
   - IFileService (Scoped)
3. Controller setup
4. CORS configuration
5. Request size limits
```

### 3. Middleware Pipeline (`ProgramExtensions.cs:96-142`)
**Order matters! Each request flows through these in sequence:**
```
1. Static Files (serves CSS/JS/HTML from wwwroot)
2. Security Middleware (validates requests, rate limiting)
3. Exception Handling (dev vs production)
4. HTTPS Redirection
5. CORS (cross-origin requests)
6. Controllers (API endpoints)
```

## API Request Flow

### Example: GET /api/files?path=/some/folder

#### 1. Request Processing (`FilesController.cs:53-92`)
```
Request → Security Validation → File Service → Response
```

**Step-by-step:**
1. **Security Check** (`FilesController.cs:60`): `_securityService.ValidateAndSanitizePath(path)`
   - Prevents path traversal attacks
   - Sanitizes user input
   - Returns validation result

2. **Business Logic** (`FilesController.cs:73`): `_fileService.GetFilesAsync(directoryPath)`
   - Delegates to service layer
   - Handles file system operations

3. **Response** (`FilesController.cs:83`): Returns JSON with file list

#### 2. Service Layer
- **IFileService** interface defines operations
- **FileSystemService** implements actual file operations
- Returns **FileListResponse** model

#### 3. Data Models (`Models/FileItem.cs`)
- **FileItem**: Represents a single file/folder
- **FileListResponse**: Contains file list + metadata

## Frontend Flow

### 1. Application Initialization (`wwwroot/src/index.js`)
```
1. Import FileBrowserDialog component
2. Create singleton instance
3. Setup browser integration (URL sync, history)
4. Expose global openDialog() function
```

### 2. Component Architecture

#### FileBrowserDialog (`FileBrowserDialog.js:23`)
**Main orchestrator component**
- **Dependencies**: DialogWidget, FileApiService, FileListRenderer
- **State**: currentPath, defaultPath, selectedFile
- **Event delegation** for performance

#### Component Hierarchy:
```
FileBrowserDialog (main coordinator)
├── DialogWidget (modal dialog)
├── FileApiService (HTTP communication)
├── FileListRenderer (UI rendering)
└── ToolbarTemplate (HTML generation)
```

### 3. User Interaction Flow

#### Opening the Dialog:
```
1. User clicks button → window.openDialog()
2. FileBrowserDialog.open() → loads default directory
3. API call → GET /api/files
4. Render file list in UI
5. Setup event handlers
```

#### Navigating Folders:
```
1. User clicks folder → File click handler
2. loadFiles(folderPath) → API call
3. Update currentPath state
4. Update browser URL
5. Render new file list
```

#### File Operations:
```
Upload: File input → upload() → POST /api/files/upload
Download: Click file → download() → GET /api/files/download
Search: Type term → search() → GET /api/files/search
```

## Key Design Patterns

### Backend:
- **Dependency Injection**: Controllers receive services via constructor
- **Result Pattern**: FileListResponse wraps success/error states
- **Security-First**: Every input validated before processing
- **Separation of Concerns**: Controllers → Services → File System

### Frontend:
- **Component Composition**: Small, focused components
- **Event Delegation**: Single document listeners for performance
- **State Management**: Internal state + URL synchronization
- **Module Pattern**: ES6 modules for clean dependencies

## Error Handling Strategy

### Backend:
1. **Input Validation**: Security service validates all inputs
2. **Try-Catch**: Controllers catch exceptions
3. **Logging**: Structured logging for debugging
4. **Safe Responses**: Never expose internal errors to client

### Frontend:
1. **API Errors**: Caught and displayed to user
2. **Graceful Degradation**: UI remains functional on errors
3. **User Feedback**: Error messages shown in UI

## Security Considerations

### Backend:
- **Path Traversal Prevention**: All paths validated
- **Request Size Limits**: 10MB max upload
- **Rate Limiting**: Via SecurityMiddleware
- **CORS**: Specific origins only

### Frontend:
- **Input Sanitization**: Server validates all inputs
- **HTTPS Only**: Redirects all HTTP to HTTPS
- **No Sensitive Data**: No secrets in client code

## File Upload/Download Flow

### Upload:
```
1. User selects file → File input change event
2. upload() method → Creates FormData
3. POST /api/files/upload with file data
4. Server validates file + path
5. FileService.UploadFileAsync() saves file
6. Refresh file list to show new file
```

### Download:
```
1. User selects file + clicks download
2. download() method → Validates selection
3. GET /api/files/download?path=...
4. Server streams file content
5. Browser handles file download
```

## Common Debugging Points

### Backend Issues:
- Check **Security validation** first if paths aren't working
- Look at **Controller logging** for request details
- Verify **Service registration** in ProgramExtensions
- Check **File permissions** on target directories

### Frontend Issues:
- Open **browser dev tools** to see API calls
- Check **console errors** for JavaScript issues
- Verify **Event delegation** is working (click events)
- Look at **Network tab** for failed API requests

## Adding New Features

### New API Endpoint:
1. Add method to **IFileService** interface
2. Implement in **FileSystemService**
3. Add controller action in **FilesController**
4. Add security validation
5. Update frontend to call new endpoint

### New UI Feature:
1. Add button/control to **toolbarTemplate.js**
2. Add action mapping in **FileBrowserDialog** constructor
3. Implement handler method
4. Update **FileApiService** if API call needed

This guide provides the essential flow understanding needed to work effectively with this application architecture.