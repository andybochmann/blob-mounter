# BlobMounter

Mount Azure Blob Storage containers as Windows drive letters. Browse, read, write, and manage blobs directly from Windows Explorer.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/platform-Windows-0078D6)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

## Features

- Mount any Azure Blob Storage container as a local drive letter (e.g. `Z:`)
- Full read/write support — create, edit, rename, and delete files
- Optional read-only mode
- Mount a subfolder within a container
- Metadata caching for responsive directory browsing
- Connection settings saved between sessions (account key encrypted with Windows DPAPI)
- Simple single-window WPF interface

## Prerequisites

1. **Windows 10/11**
2. **[.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** (or SDK to build from source)
3. **[Dokan driver v2.x](https://github.com/dokan-dev/dokany/releases)** — install `DokanSetup.exe` from the latest release

## Getting Started

### Build from source

```bash
git clone https://github.com/andybochmann/blob-mounter.git
cd blob-mounter
dotnet build
dotnet run --project src/BlobMounter.App
```

### Usage

1. Launch the application
2. Enter your Azure Storage **Account Name** and **Account Key**
3. Enter the **Container** name (and optionally a **Subfolder** prefix)
4. Select an available **Drive Letter**
5. Click **Test Connection** to verify credentials
6. Click **Mount**
7. Open the mounted drive in Windows Explorer

To disconnect, click **Unmount** next to the drive in the app.

## Architecture

```
BlobMounter.sln
src/
  BlobMounter.Core/          # File system + Azure logic (no UI dependencies)
    Azure/                   # Azure SDK wrapper, path mapping, metadata cache
    FileSystem/              # DokanNet IDokanOperations implementation
    Models/                  # Data models

  BlobMounter.App/           # WPF application
    ViewModels/              # MVVM with CommunityToolkit.Mvvm
    Services/                # Mount orchestration, driver detection, settings

  BlobMounter.Tests/         # Unit tests (xUnit)
```

### How it works

BlobMounter uses [DokanNet](https://github.com/dokan-dev/dokan-dotnet) to create a virtual file system that translates Windows file operations into Azure Blob Storage API calls:

- **Directory listing** maps to `GetBlobsByHierarchy` with `/` delimiter
- **File read** downloads the full blob into a memory buffer on first access
- **File write** buffers changes in memory and uploads on file close
- **Rename** is implemented as server-side copy + delete (blob storage has no native rename)
- **Delete directory** recursively deletes all blobs with the matching prefix

A 30-second metadata cache reduces API calls when browsing directories.

## Known Limitations

- No file locking (Azure Blob Storage limitation)
- Rename/move of large files is slow (copy + delete)
- Files are fully downloaded on open — large files (>100 MB) use temp disk storage
- Blob names are case-sensitive; Windows paths are not — avoid name collisions
- Virtual directories have synthetic timestamps

## Running Tests

```bash
dotnet test
```

## License

[MIT](LICENSE)
