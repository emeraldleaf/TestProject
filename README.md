# File Browser Application

A web-based file browser dialog built with ASP.NET Core and JavaScript.

## Configuration

### Setting the Base Path

The application's starting directory is controlled by the `AllowedBasePath` setting in `appsettings.json`:

```json
{
  "Security": {
    "AllowedBasePath": "/Users/joshuadell/dev"
  }
}
```

To change the base directory:

1. Open `appsettings.json`
2. Update the `AllowedBasePath` value under the `Security` section
3. Restart the application

The frontend will automatically fetch and use this configured path as the starting directory.

## Running the Application

1. Start the application:
   ```bash
   dotnet run
   ```

2. Open your browser and navigate to:
   ```
   http://localhost:5120/src/index.html
   ```

## Features

- Browse files and directories
- Search files 
    - Current directory or subdirectories search options
- Upload, download, copy, and move files
- Deep links support - URL parameters for direct navigation
- all Within a self-contained and reusable dialog widget or component

