using System.Security.AccessControl;
using global::Azure;
using BlobMounter.Core.Azure;
using BlobMounter.Core.Models;
using DokanNet;
using FileAccess = DokanNet.FileAccess;

namespace BlobMounter.Core.FileSystem;

/// <summary>
/// Implements IDokanOperations to expose Azure Blob Storage as a Windows file system.
/// Strategy: download-on-open, buffer writes in memory, upload-on-close.
/// </summary>
public sealed class BlobFileSystem : IDokanOperations
{
    private readonly BlobStorageClient _client;
    private readonly PathMapper _pathMapper;
    private readonly MetadataCache _cache;
    private readonly bool _readOnly;
    private readonly string _containerName;

    public BlobFileSystem(BlobStorageClient client, PathMapper pathMapper,
        MetadataCache cache, string containerName, bool readOnly)
    {
        _client = client;
        _pathMapper = pathMapper;
        _cache = cache;
        _containerName = containerName;
        _readOnly = readOnly;
    }

    public NtStatus CreateFile(string fileName, FileAccess access, System.IO.FileShare share,
        System.IO.FileMode mode, System.IO.FileOptions options, System.IO.FileAttributes attributes,
        IDokanFileInfo info)
    {
        // Root directory always exists
        if (fileName == "\\")
        {
            info.IsDirectory = true;
            info.Context = new FileContext(string.Empty, true);
            return DokanResult.Success;
        }

        var blobPath = _pathMapper.ToBlobPath(fileName);

        try
        {
            // Handle directory requests
            if (info.IsDirectory)
            {
                if (mode == System.IO.FileMode.CreateNew)
                {
                    // "Creating" a directory in blob storage is a no-op (virtual directories)
                    if (_readOnly)
                        return DokanResult.AccessDenied;
                }

                info.IsDirectory = true;
                info.Context = new FileContext(blobPath, true);
                return DokanResult.Success;
            }

            // Try to get properties for existing file
            var itemInfo = _cache.GetItem(blobPath);
            if (itemInfo == null)
            {
                var propsTask = _client.GetPropertiesAsync(blobPath);
                propsTask.Wait();
                itemInfo = propsTask.Result;
                if (itemInfo != null)
                    _cache.SetItem(blobPath, itemInfo);
            }

            var fileExists = itemInfo != null;

            // Check if it's a directory by checking if blobs exist with this prefix
            if (!fileExists)
            {
                var dirPrefix = blobPath.EndsWith('/') ? blobPath : blobPath + "/";
                var listTask = _client.ListBlobsByHierarchyAsync(dirPrefix);
                listTask.Wait();
                if (listTask.Result.Count > 0)
                {
                    info.IsDirectory = true;
                    info.Context = new FileContext(blobPath, true);
                    return DokanResult.Success;
                }
            }

            switch (mode)
            {
                case System.IO.FileMode.Open:
                    if (!fileExists)
                        return DokanResult.FileNotFound;
                    break;

                case System.IO.FileMode.CreateNew:
                    if (fileExists)
                        return DokanResult.FileExists;
                    if (_readOnly)
                        return DokanResult.AccessDenied;
                    break;

                case System.IO.FileMode.Create:
                    if (_readOnly)
                        return DokanResult.AccessDenied;
                    break;

                case System.IO.FileMode.OpenOrCreate:
                    if (_readOnly && !fileExists)
                        return DokanResult.AccessDenied;
                    break;

                case System.IO.FileMode.Truncate:
                    if (!fileExists)
                        return DokanResult.FileNotFound;
                    if (_readOnly)
                        return DokanResult.AccessDenied;
                    break;

                case System.IO.FileMode.Append:
                    if (_readOnly)
                        return DokanResult.AccessDenied;
                    break;
            }

            var ctx = new FileContext(blobPath, false);

            // For creation modes on new files, initialize an empty buffer
            if (!fileExists && (mode == System.IO.FileMode.CreateNew ||
                                mode == System.IO.FileMode.Create ||
                                mode == System.IO.FileMode.OpenOrCreate))
            {
                ctx.InitializeEmpty();
                ctx.IsDirty = true;
            }

            // For truncate or create-over-existing, start fresh
            if (fileExists && (mode == System.IO.FileMode.Create ||
                               mode == System.IO.FileMode.Truncate))
            {
                ctx.InitializeEmpty();
                ctx.IsDirty = true;
            }

            info.Context = ctx;
            return DokanResult.Success;
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException rfe)
        {
            return MapAzureException(rfe);
        }
        catch (RequestFailedException rfe)
        {
            return MapAzureException(rfe);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"CreateFile error for {fileName}: {ex}");
            return DokanResult.InternalError;
        }
    }

    public void Cleanup(string fileName, IDokanFileInfo info)
    {
        var ctx = info.Context as FileContext;
        if (ctx == null) return;

        try
        {
            if (info.DeletePending)
            {
                if (ctx.IsDirectory)
                {
                    var prefix = _pathMapper.GetListPrefix(fileName);
                    var task = _client.ListAllBlobsAsync(prefix);
                    task.Wait();
                    foreach (var blob in task.Result)
                    {
                        _client.DeleteAsync(blob).Wait();
                    }
                    _cache.InvalidatePrefix(prefix);
                }
                else
                {
                    _client.DeleteAsync(ctx.BlobPath).Wait();
                    _cache.InvalidateItem(ctx.BlobPath);
                }

                var parentPrefix = GetParentPrefix(ctx.BlobPath);
                _cache.InvalidatePrefix(parentPrefix);
            }
            else if (ctx.IsDirty && !_readOnly && !string.IsNullOrEmpty(ctx.BlobPath))
            {
                var stream = ctx.GetReadStream();
                _client.UploadAsync(ctx.BlobPath, stream, overwrite: true).Wait();
                _cache.InvalidateItem(ctx.BlobPath);

                var parentPrefix = GetParentPrefix(ctx.BlobPath);
                _cache.InvalidatePrefix(parentPrefix);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Cleanup error for {fileName}: {ex}");
        }
    }

    public void CloseFile(string fileName, IDokanFileInfo info)
    {
        var ctx = info.Context as FileContext;
        ctx?.Dispose();
        info.Context = null;
    }

    public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset,
        IDokanFileInfo info)
    {
        bytesRead = 0;

        var ctx = info.Context as FileContext;
        if (ctx == null || ctx.IsDirectory)
            return DokanResult.AccessDenied;

        try
        {
            // Download on first read if buffer not loaded
            if (ctx.Buffer == null)
            {
                var downloadTask = _client.DownloadAsync(ctx.BlobPath);
                downloadTask.Wait();

                var props = _cache.GetItem(ctx.BlobPath);
                var size = props?.Size ?? 0;
                ctx.LoadContentAsync(downloadTask.Result, size).Wait();
            }

            bytesRead = ctx.Read(buffer, offset, buffer.Length);
            return DokanResult.Success;
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException rfe)
        {
            return MapAzureException(rfe);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"ReadFile error for {fileName}: {ex}");
            return DokanResult.InternalError;
        }
    }

    public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset,
        IDokanFileInfo info)
    {
        bytesWritten = 0;

        if (_readOnly)
            return DokanResult.AccessDenied;

        var ctx = info.Context as FileContext;
        if (ctx == null || ctx.IsDirectory)
            return DokanResult.AccessDenied;

        try
        {
            // Download existing content on first write if not yet loaded
            if (ctx.Buffer == null)
            {
                var exists = _client.ExistsAsync(ctx.BlobPath).GetAwaiter().GetResult();
                if (exists)
                {
                    var downloadTask = _client.DownloadAsync(ctx.BlobPath);
                    downloadTask.Wait();
                    var props = _cache.GetItem(ctx.BlobPath);
                    var size = props?.Size ?? 0;
                    ctx.LoadContentAsync(downloadTask.Result, size).Wait();
                }
                else
                {
                    ctx.InitializeEmpty();
                }
            }

            if (info.WriteToEndOfFile)
                offset = ctx.Length;

            ctx.Write(buffer, offset, buffer.Length);
            bytesWritten = buffer.Length;
            return DokanResult.Success;
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException rfe)
        {
            return MapAzureException(rfe);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"WriteFile error for {fileName}: {ex}");
            return DokanResult.InternalError;
        }
    }

    public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

    public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo,
        IDokanFileInfo info)
    {
        fileInfo = new FileInformation();

        if (fileName == "\\")
        {
            fileInfo = new FileInformation
            {
                FileName = "\\",
                Attributes = System.IO.FileAttributes.Directory,
                CreationTime = DateTime.Now,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                Length = 0,
            };
            return DokanResult.Success;
        }

        var ctx = info.Context as FileContext;

        if (ctx?.IsDirectory == true)
        {
            fileInfo = new FileInformation
            {
                FileName = Path.GetFileName(fileName),
                Attributes = System.IO.FileAttributes.Directory,
                CreationTime = DateTime.Now,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                Length = 0,
            };
            return DokanResult.Success;
        }

        // If we have a context with a buffer, the file is being actively written —
        // return info from the buffer, not Azure (it may not be uploaded yet).
        if (ctx != null && !ctx.IsDirectory && ctx.Buffer != null)
        {
            fileInfo = new FileInformation
            {
                FileName = Path.GetFileName(fileName),
                Attributes = _readOnly
                    ? System.IO.FileAttributes.ReadOnly | System.IO.FileAttributes.Archive
                    : System.IO.FileAttributes.Archive,
                Length = ctx.Length,
                CreationTime = DateTime.Now,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
            };
            return DokanResult.Success;
        }

        try
        {
            var blobPath = _pathMapper.ToBlobPath(fileName);

            var itemInfo = _cache.GetItem(blobPath);
            if (itemInfo == null)
            {
                var propsTask = _client.GetPropertiesAsync(blobPath);
                propsTask.Wait();
                itemInfo = propsTask.Result;

                if (itemInfo == null)
                {
                    // Maybe it's a directory prefix
                    var dirPrefix = blobPath.EndsWith('/') ? blobPath : blobPath + "/";
                    var listTask = _client.ListBlobsByHierarchyAsync(dirPrefix);
                    listTask.Wait();
                    if (listTask.Result.Count > 0)
                    {
                        fileInfo = new FileInformation
                        {
                            FileName = Path.GetFileName(fileName),
                            Attributes = System.IO.FileAttributes.Directory,
                            CreationTime = DateTime.Now,
                            LastAccessTime = DateTime.Now,
                            LastWriteTime = DateTime.Now,
                            Length = 0,
                        };
                        return DokanResult.Success;
                    }

                    // File has a context (was opened/created) but buffer is null yet —
                    // it's a newly created file that hasn't been written to yet
                    if (ctx != null && !ctx.IsDirectory)
                    {
                        fileInfo = new FileInformation
                        {
                            FileName = Path.GetFileName(fileName),
                            Attributes = System.IO.FileAttributes.Archive,
                            Length = 0,
                            CreationTime = DateTime.Now,
                            LastAccessTime = DateTime.Now,
                            LastWriteTime = DateTime.Now,
                        };
                        return DokanResult.Success;
                    }

                    return DokanResult.FileNotFound;
                }

                _cache.SetItem(blobPath, itemInfo);
            }

            fileInfo = new FileInformation
            {
                FileName = itemInfo.Name,
                Attributes = _readOnly
                    ? System.IO.FileAttributes.ReadOnly | System.IO.FileAttributes.Archive
                    : System.IO.FileAttributes.Archive,
                Length = ctx?.Buffer != null ? ctx.Length : itemInfo.Size,
                CreationTime = itemInfo.LastModified.LocalDateTime,
                LastAccessTime = itemInfo.LastModified.LocalDateTime,
                LastWriteTime = itemInfo.LastModified.LocalDateTime,
            };
            return DokanResult.Success;
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException rfe)
        {
            return MapAzureException(rfe);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"GetFileInformation error for {fileName}: {ex}");
            return DokanResult.InternalError;
        }
    }

    public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
    {
        files = new List<FileInformation>();

        try
        {
            var prefix = _pathMapper.GetListPrefix(fileName);

            var cached = _cache.GetListing(prefix);
            IReadOnlyList<BlobItemInfo> items;

            if (cached != null)
            {
                items = cached;
            }
            else
            {
                var task = _client.ListBlobsByHierarchyAsync(prefix);
                task.Wait();
                items = task.Result;
                _cache.SetListing(prefix, items);
            }

            foreach (var item in items)
            {
                var fi = new FileInformation
                {
                    FileName = item.Name,
                    Length = item.Size,
                    CreationTime = item.LastModified.LocalDateTime,
                    LastAccessTime = item.LastModified.LocalDateTime,
                    LastWriteTime = item.LastModified.LocalDateTime,
                    Attributes = item.IsDirectory
                        ? System.IO.FileAttributes.Directory
                        : (_readOnly
                            ? System.IO.FileAttributes.ReadOnly | System.IO.FileAttributes.Archive
                            : System.IO.FileAttributes.Archive),
                };
                files.Add(fi);
                _cache.SetItem(item.FullPath, item);
            }

            return DokanResult.Success;
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException rfe)
        {
            return MapAzureException(rfe);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"FindFiles error for {fileName}: {ex}");
            return DokanResult.InternalError;
        }
    }

    public NtStatus FindFilesWithPattern(string fileName, string searchPattern,
        out IList<FileInformation> files, IDokanFileInfo info)
    {
        files = new List<FileInformation>();
        return FindFiles(fileName, out files, info);
    }

    public NtStatus SetFileAttributes(string fileName, System.IO.FileAttributes attributes,
        IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

    public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
        DateTime? lastWriteTime, IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

    public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
    {
        if (_readOnly)
            return DokanResult.AccessDenied;

        // Actual deletion happens in Cleanup when DeletePending is true
        // Just verify the file exists (or has a context — could be a new file)
        var ctx = info.Context as FileContext;
        if (ctx != null && !ctx.IsDirectory)
            return DokanResult.Success;

        var blobPath = _pathMapper.ToBlobPath(fileName);
        var exists = _client.ExistsAsync(blobPath).GetAwaiter().GetResult();
        return exists ? DokanResult.Success : DokanResult.FileNotFound;
    }

    public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
    {
        if (_readOnly)
            return DokanResult.AccessDenied;

        return DokanResult.Success;
    }

    public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
    {
        if (_readOnly)
            return DokanResult.AccessDenied;

        try
        {
            var oldBlobPath = _pathMapper.ToBlobPath(oldName);
            var newBlobPath = _pathMapper.ToBlobPath(newName);

            var ctx = info.Context as FileContext;

            if (ctx?.IsDirectory == true)
            {
                var oldPrefix = oldBlobPath.EndsWith('/') ? oldBlobPath : oldBlobPath + "/";
                var newPrefix = newBlobPath.EndsWith('/') ? newBlobPath : newBlobPath + "/";

                var task = _client.ListAllBlobsAsync(oldPrefix);
                task.Wait();
                var blobs = task.Result;

                foreach (var blob in blobs)
                {
                    var newPath = newPrefix + blob[oldPrefix.Length..];
                    _client.CopyAsync(blob, newPath).Wait();
                    _client.DeleteAsync(blob).Wait();
                }

                _cache.InvalidatePrefix(oldPrefix);
                _cache.InvalidatePrefix(newPrefix);
            }
            else
            {
                if (!replace)
                {
                    var existsTask = _client.ExistsAsync(newBlobPath);
                    existsTask.Wait();
                    if (existsTask.Result)
                        return DokanResult.FileExists;
                }

                _client.CopyAsync(oldBlobPath, newBlobPath).Wait();
                _client.DeleteAsync(oldBlobPath).Wait();

                _cache.InvalidateItem(oldBlobPath);
                _cache.InvalidateItem(newBlobPath);
            }

            _cache.InvalidatePrefix(GetParentPrefix(oldBlobPath));
            _cache.InvalidatePrefix(GetParentPrefix(newBlobPath));

            return DokanResult.Success;
        }
        catch (AggregateException ex) when (ex.InnerException is RequestFailedException rfe)
        {
            return MapAzureException(rfe);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"MoveFile error for {oldName} -> {newName}: {ex}");
            return DokanResult.InternalError;
        }
    }

    public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
    {
        if (_readOnly)
            return DokanResult.AccessDenied;

        var ctx = info.Context as FileContext;
        if (ctx == null) return DokanResult.InvalidHandle;

        ctx.SetLength(length);
        return DokanResult.Success;
    }

    public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
    {
        if (_readOnly)
            return DokanResult.AccessDenied;

        var ctx = info.Context as FileContext;
        if (ctx == null) return DokanResult.InvalidHandle;

        // SetAllocationSize is about reserving space, not truncating.
        // Only extend the buffer if the requested size is larger than current.
        if (ctx.Buffer == null)
            ctx.InitializeEmpty();

        if (length > ctx.Length)
        {
            ctx.Buffer!.SetLength(length);
            // Don't update ctx.Length or mark dirty — this is just a space reservation.
            // The actual content length is tracked separately via writes.
        }

        return DokanResult.Success;
    }

    public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
    {
        return DokanResult.NotImplemented;
    }

    public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
    {
        return DokanResult.NotImplemented;
    }

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes,
        out long totalNumberOfFreeBytes, IDokanFileInfo info)
    {
        const long oneTB = 1L * 1024 * 1024 * 1024 * 1024;
        freeBytesAvailable = oneTB;
        totalNumberOfBytes = oneTB;
        totalNumberOfFreeBytes = oneTB;
        return DokanResult.Success;
    }

    public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
        out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
    {
        volumeLabel = $"Azure:{_containerName}";
        fileSystemName = "BlobFS";
        maximumComponentLength = 256;
        features = FileSystemFeatures.CasePreservedNames
                   | FileSystemFeatures.CaseSensitiveSearch
                   | FileSystemFeatures.UnicodeOnDisk;
        return DokanResult.Success;
    }

    public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security,
        AccessControlSections sections, IDokanFileInfo info)
    {
        security = null;
        return DokanResult.NotImplemented;
    }

    public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security,
        AccessControlSections sections, IDokanFileInfo info)
    {
        return DokanResult.NotImplemented;
    }

    public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

    public NtStatus Unmounted(IDokanFileInfo info)
    {
        _cache.Clear();
        return DokanResult.Success;
    }

    public NtStatus FindStreams(string fileName, out IList<FileInformation> streams,
        IDokanFileInfo info)
    {
        streams = new List<FileInformation>();
        return DokanResult.NotImplemented;
    }

    private static NtStatus MapAzureException(RequestFailedException ex)
    {
        return ex.Status switch
        {
            404 => DokanResult.FileNotFound,
            403 => DokanResult.AccessDenied,
            409 => DokanResult.SharingViolation,
            412 => DokanResult.SharingViolation,
            416 => DokanResult.InvalidParameter,
            503 => DokanResult.InternalError,
            _ => DokanResult.InternalError,
        };
    }

    private static string GetParentPrefix(string blobPath)
    {
        var trimmed = blobPath.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[..(lastSlash + 1)] : string.Empty;
    }
}
