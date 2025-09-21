
## Option 1: Jest + jsdom (Recommended for ES6)

### Step 1: Install Testing Dependencies
```bash
npm install --save-dev jest jsdom @babel/core @babel/preset-env babel-jest
```

### Step 2: Configure Babel (`.babelrc` or `babel.config.js`)
````javascript
// babel.config.js
module.exports = {
  presets: [
    ['@babel/preset-env', {
      targets: {
        node: 'current'
      }
    }]
  ]
};
````

### Step 3: Configure Jest (`jest.config.js`)
````javascript
module.exports = {
  testEnvironment: 'jsdom',
  setupFilesAfterEnv: ['<rootDir>/tests/setup.js'],
  testMatch: [
    '**/tests/**/*.test.js',
    '**/tests/**/*.spec.js'
  ],
  collectCoverageFrom: [
    'src/**/*.js',
    '!src/**/*.test.js'
  ],
  transform: {
    '^.+\\.js$': 'babel-jest'
  }
};
````

### Step 4: Create Test Setup File (`tests/setup.js`)
````javascript
// tests/setup.js
// Global test setup
global.fetch = require('jest-fetch-mock');

// Mock DOM APIs if needed
Object.defineProperty(window, 'localStorage', {
  value: {
    getItem: jest.fn(),
    setItem: jest.fn(),
    removeItem: jest.fn(),
    clear: jest.fn(),
  },
  writable: true,
});
````

### Step 5: Example Test Structure
````javascript
// tests/fileManager.test.js
import { FileManager } from '../src/fileManager.js';

describe('FileManager', () => {
  let fileManager;
  let mockContainer;

  beforeEach(() => {
    // Create a mock DOM container
    document.body.innerHTML = '<div id="file-container"></div>';
    mockContainer = document.getElementById('file-container');
    fileManager = new FileManager(mockContainer);
  });

  afterEach(() => {
    document.body.innerHTML = '';
    jest.clearAllMocks();
  });

  test('should initialize with empty file list', () => {
    expect(fileManager.files).toEqual([]);
    expect(mockContainer.children.length).toBe(0);
  });

  test('should render files when provided', async () => {
    const mockFiles = [
      { name: 'test.txt', isDirectory: false, size: 1024 },
      { name: 'folder', isDirectory: true, size: 0 }
    ];

    await fileManager.renderFiles(mockFiles);

    expect(mockContainer.querySelectorAll('.file-item')).toHaveLength(2);
    expect(mockContainer.textContent).toContain('test.txt');
    expect(mockContainer.textContent).toContain('folder');
  });

  test('should handle API calls', async () => {
    global.fetch = jest.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        files: [{ name: 'test.txt', isDirectory: false }],
        success: true
      })
    });

    const result = await fileManager.loadFiles('/test/path');

    expect(fetch).toHaveBeenCalledWith('/api/files?path=/test/path');
    expect(result.success).toBe(true);
  });

  test('should handle search functionality', async () => {
    const searchSpy = jest.spyOn(fileManager, 'searchFiles');
    
    // Simulate user input
    const searchInput = document.createElement('input');
    searchInput.value = '*.txt';
    mockContainer.appendChild(searchInput);

    const searchButton = document.createElement('button');
    searchButton.onclick = () => fileManager.searchFiles(searchInput.value);
    mockContainer.appendChild(searchButton);

    searchButton.click();

    expect(searchSpy).toHaveBeenCalledWith('*.txt');
  });
});
````

### Step 6: Package.json Scripts
````json
{
  "scripts": {
    "test": "jest",
    "test:watch": "jest --watch",
    "test:coverage": "jest --coverage",
    "test:debug": "node --inspect-brk node_modules/.bin/jest --runInBand"
  }
}
````

## Option 2: Vitest (Modern Alternative)

### Install Vitest:
```bash
npm install --save-dev vitest jsdom
```

### Configure Vitest (`vitest.config.js`):
````javascript
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./tests/setup.js']
  }
});
````

### Package.json Scripts for Vitest:
````json
{
  "scripts": {
    "test": "vitest",
    "test:ui": "vitest --ui",
    "test:coverage": "vitest --coverage"
  }
}
````

## Example ES6 Module to Test

````javascript
// src/fileManager.js
export class FileManager {
  constructor(container) {
    this.container = container;
    this.files = [];
    this.currentPath = '/';
  }

  async loadFiles(path = '/') {
    try {
      const response = await fetch(`/api/files?path=${encodeURIComponent(path)}`);
      const data = await response.json();
      
      if (data.success) {
        this.files = data.files;
        this.currentPath = path;
        await this.renderFiles(this.files);
        return data;
      }
      throw new Error(data.errorMessage);
    } catch (error) {
      console.error('Failed to load files:', error);
      throw error;
    }
  }

  async renderFiles(files) {
    this.container.innerHTML = '';
    
    files.forEach(file => {
      const fileElement = document.createElement('div');
      fileElement.className = 'file-item';
      fileElement.textContent = file.name;
      
      if (file.isDirectory) {
        fileElement.classList.add('directory');
        fileElement.onclick = () => this.loadFiles(`${this.currentPath}/${file.name}`);
      }
      
      this.container.appendChild(fileElement);
    });
  }

  async searchFiles(term, includeSubdirectories = true) {
    const params = new URLSearchParams({
      path: this.currentPath,
      term,
      includeSubdirectories
    });

    const response = await fetch(`/api/files/search?${params}`);
    const data = await response.json();
    
    if (data.success) {
      await this.renderFiles(data.files);
    }
    
    return data;
  }
}
````

## Running Tests

```bash
# Run all tests
npm test

# Run tests in watch mode
npm run test:watch

# Run with coverage
npm run test:coverage
```

This setup gives you:
- **ES6 module support** via Babel
- **DOM testing** via jsdom
- **API mocking** with jest mocks
- **Clean test structure** with setup/teardown
- **Coverage reporting**
- **Watch mode** for development

The configuration is simple, standard, and works well with modern ES6 JavaScript applications.