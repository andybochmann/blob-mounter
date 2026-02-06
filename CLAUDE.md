# CLAUDE.md

## Project Overview
BlobMounter is a .NET 8 WPF application that mounts Azure Blob Storage containers as Windows drive letters using DokanNet. Authentication is via storage account name + key.

## Build & Test
```bash
dotnet build              # Build all projects
dotnet test               # Run unit tests (24 tests: PathMapper + MetadataCache)
dotnet run --project src/BlobMounter.App   # Run the app
```

### Publish a release build
```bash
dotnet publish src/BlobMounter.App/BlobMounter.App.csproj -c Release -r win-x64 --no-self-contained -o publish/BlobMounter
```

## Solution Structure
- **BlobMounter.Core** — Azure SDK wrapper + DokanNet `IDokanOperations` implementation (no UI dependencies)
- **BlobMounter.App** — WPF UI with MVVM (CommunityToolkit.Mvvm) + DI (Microsoft.Extensions.DependencyInjection)
- **BlobMounter.Tests** — xUnit tests for PathMapper and MetadataCache

## Key Technical Notes
- DokanNet 2.3.0.3 uses the legacy `IDokanOperations` interface (not IDokanOperations2)
- `ILogger` in MountService.cs is `DokanNet.Logging.ILogger`, not Microsoft.Extensions
- **Namespace conflict**: `BlobMounter.Core.Azure` collides with the `Azure` SDK namespace — use `global::Azure` when referencing Azure SDK types from `BlobMounter.Core.FileSystem`
- WPF projects need explicit `using System.IO` despite `ImplicitUsings=enable`
- `PasswordBox` doesn't support data binding — uses code-behind `PasswordChanged` event handler
- Account key is encrypted with Windows DPAPI (`System.Security.Cryptography.ProtectedData`)
- Settings stored at `%APPDATA%\BlobMounter\settings.json`

## File System Design
- **Strategy**: Download-on-open, buffer writes in memory, upload-on-close
- Files >100MB use temp files on disk instead of MemoryStream
- 30-second TTL metadata cache reduces Azure API calls
- `CreateFile` must initialize an empty buffer for new files (Create/CreateNew modes) — otherwise `GetFileInformation` returns FileNotFound before upload
- `SetAllocationSize` reserves buffer space without marking dirty — distinct from `SetEndOfFile` which sets logical length
- Rename = server-side copy + delete (blob storage has no native rename)
- Virtual directories are synthetic (blob storage only has flat key-value blobs)

## Runtime Requirements
- Dokan driver v2.x must be installed on the user's machine ([dokany releases](https://github.com/dokan-dev/dokany/releases))
- App detects missing driver via `dokan2.dll` in System32
