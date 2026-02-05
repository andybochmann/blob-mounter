namespace BlobMounter.Core.FileSystem;

/// <summary>
/// Per-handle state stored in IDokanFileInfo.Context.
/// Holds the file content buffer and tracks whether it has been modified.
/// </summary>
public sealed class FileContext : IDisposable
{
    private const long LargeFileThreshold = 100 * 1024 * 1024; // 100 MB

    public string BlobPath { get; }
    public bool IsDirectory { get; }
    public bool IsDirty { get; set; }
    public Stream? Buffer { get; private set; }
    public long Length { get; private set; }

    private bool _disposed;

    public FileContext(string blobPath, bool isDirectory)
    {
        BlobPath = blobPath;
        IsDirectory = isDirectory;
    }

    /// <summary>
    /// Loads the blob content into the buffer. Uses MemoryStream for small files
    /// and a temp file for files larger than 100 MB.
    /// </summary>
    public async Task LoadContentAsync(Stream source, long size)
    {
        if (size > LargeFileThreshold)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "BlobMounter", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.DeleteOnClose);
            await source.CopyToAsync(fileStream);
            fileStream.Position = 0;
            Buffer = fileStream;
        }
        else
        {
            var ms = new MemoryStream();
            await source.CopyToAsync(ms);
            ms.Position = 0;
            Buffer = ms;
        }

        Length = Buffer.Length;
    }

    /// <summary>
    /// Initializes an empty buffer for new file creation.
    /// </summary>
    public void InitializeEmpty()
    {
        Buffer = new MemoryStream();
        Length = 0;
    }

    public int Read(byte[] buffer, long offset, int count)
    {
        if (Buffer == null) return 0;

        lock (Buffer)
        {
            Buffer.Position = offset;
            return Buffer.Read(buffer, 0, count);
        }
    }

    public void Write(byte[] buffer, long offset, int count)
    {
        if (Buffer == null)
            InitializeEmpty();

        lock (Buffer!)
        {
            Buffer.Position = offset;
            Buffer.Write(buffer, 0, count);
            Length = Math.Max(Length, Buffer.Position);
            IsDirty = true;
        }
    }

    public void SetLength(long length)
    {
        if (Buffer == null)
            InitializeEmpty();

        Buffer!.SetLength(length);
        Length = length;
        IsDirty = true;
    }

    public Stream GetReadStream()
    {
        if (Buffer == null)
            return Stream.Null;

        Buffer.Position = 0;
        return Buffer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Buffer?.Dispose();
    }
}
