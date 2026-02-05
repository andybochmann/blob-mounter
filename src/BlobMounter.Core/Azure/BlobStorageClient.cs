using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BlobMounter.Core.Models;

namespace BlobMounter.Core.Azure;

/// <summary>
/// Wraps the Azure BlobContainerClient to provide simplified operations for the file system layer.
/// </summary>
public sealed class BlobStorageClient
{
    private readonly BlobContainerClient _container;

    public BlobStorageClient(string accountName, string accountKey, string containerName)
    {
        var uri = new Uri($"https://{accountName}.blob.core.windows.net/{containerName}");
        var credential = new StorageSharedKeyCredential(accountName, accountKey);
        _container = new BlobContainerClient(uri, credential);
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _container.GetPropertiesAsync(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<BlobItemInfo>> ListBlobsByHierarchyAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        var items = new List<BlobItemInfo>();

        await foreach (var item in _container.GetBlobsByHierarchyAsync(
            delimiter: "/", prefix: prefix, cancellationToken: cancellationToken))
        {
            if (item.IsPrefix)
            {
                var dirName = PathMapper.GetName(item.Prefix);
                items.Add(new BlobItemInfo
                {
                    Name = dirName,
                    FullPath = item.Prefix,
                    IsDirectory = true,
                    LastModified = DateTimeOffset.UtcNow,
                });
            }
            else if (item.IsBlob)
            {
                var blob = item.Blob;
                items.Add(new BlobItemInfo
                {
                    Name = blob.Name[(blob.Name.LastIndexOf('/') + 1)..],
                    FullPath = blob.Name,
                    Size = blob.Properties.ContentLength ?? 0,
                    LastModified = blob.Properties.LastModified ?? DateTimeOffset.UtcNow,
                    IsDirectory = false,
                    ETag = blob.Properties.ETag?.ToString(),
                });
            }
        }

        return items;
    }

    public async Task<Stream> DownloadAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task UploadAsync(string blobPath, Stream content, bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        await blobClient.UploadAsync(content, overwrite: overwrite, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<BlobItemInfo?> GetPropertiesAsync(string blobPath,
        CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        try
        {
            var props = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new BlobItemInfo
            {
                Name = PathMapper.GetName(blobPath),
                FullPath = blobPath,
                Size = props.Value.ContentLength,
                LastModified = props.Value.LastModified,
                IsDirectory = false,
                ETag = props.Value.ETag.ToString(),
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task CopyAsync(string sourcePath, string destPath,
        CancellationToken cancellationToken = default)
    {
        var sourceClient = _container.GetBlobClient(sourcePath);
        var destClient = _container.GetBlobClient(destPath);
        var operation = await destClient.StartCopyFromUriAsync(sourceClient.Uri, cancellationToken: cancellationToken);
        await operation.WaitForCompletionAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }

    /// <summary>
    /// Lists all blobs with the given prefix (recursive) for operations like directory delete.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListAllBlobsAsync(string prefix,
        CancellationToken cancellationToken = default)
    {
        var paths = new List<string>();
        await foreach (var blob in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            paths.Add(blob.Name);
        }
        return paths;
    }
}
