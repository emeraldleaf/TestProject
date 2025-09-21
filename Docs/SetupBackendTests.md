# TODO setup backend tests

## Step 1: Navigate to Root Directory
```bash
cd /Users/joshuadell/Dev/MapLarge/interview
```

## Step 2: Create the Test Project
```bash
dotnet new xunit -n TestProject.Tests
```

## Step 3: Add Test Project to Existing Solution
```bash
dotnet sln add TestProject.Tests/TestProject.Tests.csproj
```

## Step 4: Add Project Reference
```bash
cd TestProject.Tests
dotnet add reference ../TestProject/TestProject.csproj
```

## Step 5: Install Testing Packages
```bash
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package NSubstitute
dotnet add package FluentAssertions
```

## Step 6: Verify Solution Structure
```bash
cd ..
dotnet sln list
```

You should see both projects listed.

## Step 7: Test the Setup
```bash
dotnet build
dotnet test
```


## Key Differences with NSubstitute

1. **Creation**: `Substitute.For<T>()` instead of `new Mock<T>()`
2. **Access**: Direct interface access instead of `.Object` property
3. **Setup**: More fluent syntax for method setup
4. **Verification**: Different syntax for verifying calls

## Example Test with NSubstitute Features

````csharp
[Fact]
public async Task GetFiles_ValidPath_ReturnsOkResult()
{
    // Arrange
    var testPath = "/test/path";
    var pathValidation = new PathValidationResult(true, testPath, null);
    var fileResponse = new FileListResponse([], testPath, true, "Success");
    
    _securityService.ValidateAndSanitizePath(testPath).Returns(pathValidation);
    _fileService.GetFilesAsync(testPath).Returns(fileResponse);

    // Act
    var result = await _controller.GetFiles(testPath);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.Equal(fileResponse, okResult.Value);
    
    // Verify calls were made
    _securityService.Received(1).ValidateAndSanitizePath(testPath);
    await _fileService.Received(1).GetFilesAsync(testPath);
}
````

NSubstitute generally provides a cleaner, more readable syntax compared to Moq, especially for simple mocking scenarios.