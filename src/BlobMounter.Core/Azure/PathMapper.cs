namespace BlobMounter.Core.Azure;

/// <summary>
/// Converts between Windows paths (backslash-separated) and blob paths (forward-slash-separated),
/// optionally prepending a subfolder prefix.
/// </summary>
public sealed class PathMapper
{
    private readonly string _prefix;

    public PathMapper(string? subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder))
        {
            _prefix = string.Empty;
        }
        else
        {
            _prefix = subfolder.Trim('/').Replace('\\', '/');
            if (_prefix.Length > 0)
                _prefix += "/";
        }
    }

    /// <summary>
    /// Converts a Windows path like "\folder\file.txt" to a blob path like "prefix/folder/file.txt".
    /// </summary>
    public string ToBlobPath(string windowsPath)
    {
        var cleaned = windowsPath.TrimStart('\\').Replace('\\', '/');
        return _prefix + cleaned;
    }

    /// <summary>
    /// Converts a blob path like "prefix/folder/file.txt" to a Windows path like "\folder\file.txt".
    /// </summary>
    public string ToWindowsPath(string blobPath)
    {
        var relative = blobPath;
        if (_prefix.Length > 0 && relative.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[_prefix.Length..];
        }

        return "\\" + relative.Replace('/', '\\');
    }

    /// <summary>
    /// Gets the blob prefix for listing blobs in a directory.
    /// For root ("\"), returns the subfolder prefix. For "\folder\", returns "prefix/folder/".
    /// </summary>
    public string GetListPrefix(string windowsPath)
    {
        var cleaned = windowsPath.TrimStart('\\').Replace('\\', '/');
        var prefix = _prefix + cleaned;
        if (prefix.Length > 0 && !prefix.EndsWith('/'))
            prefix += "/";
        return prefix;
    }

    /// <summary>
    /// Gets the blob name (last segment) from a blob path like "folder/file.txt" → "file.txt".
    /// Also handles directory prefixes like "folder/" → "folder".
    /// </summary>
    public static string GetName(string blobPath)
    {
        var trimmed = blobPath.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }
}
