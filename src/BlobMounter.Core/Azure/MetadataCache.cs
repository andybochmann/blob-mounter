using System.Collections.Concurrent;
using BlobMounter.Core.Models;

namespace BlobMounter.Core.Azure;

/// <summary>
/// Short-lived cache for blob metadata and directory listings to reduce Azure API calls.
/// Entries expire after a configurable TTL (default 30 seconds).
/// </summary>
public sealed class MetadataCache
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, CacheEntry<BlobItemInfo>> _itemCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<BlobItemInfo>>> _listCache = new();

    public MetadataCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(30);
    }

    public BlobItemInfo? GetItem(string blobPath)
    {
        if (_itemCache.TryGetValue(blobPath, out var entry) && !entry.IsExpired(_ttl))
            return entry.Value;

        _itemCache.TryRemove(blobPath, out _);
        return null;
    }

    public void SetItem(string blobPath, BlobItemInfo info)
    {
        _itemCache[blobPath] = new CacheEntry<BlobItemInfo>(info);
    }

    public IReadOnlyList<BlobItemInfo>? GetListing(string prefix)
    {
        if (_listCache.TryGetValue(prefix, out var entry) && !entry.IsExpired(_ttl))
            return entry.Value;

        _listCache.TryRemove(prefix, out _);
        return null;
    }

    public void SetListing(string prefix, IReadOnlyList<BlobItemInfo> items)
    {
        _listCache[prefix] = new CacheEntry<IReadOnlyList<BlobItemInfo>>(items);
    }

    public void InvalidateItem(string blobPath)
    {
        _itemCache.TryRemove(blobPath, out _);
    }

    public void InvalidatePrefix(string prefix)
    {
        // Remove all listings that start with or match the prefix
        foreach (var key in _listCache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                prefix.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                _listCache.TryRemove(key, out _);
            }
        }

        // Also remove individual items under this prefix
        foreach (var key in _itemCache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                _itemCache.TryRemove(key, out _);
            }
        }
    }

    public void Clear()
    {
        _itemCache.Clear();
        _listCache.Clear();
    }

    private sealed class CacheEntry<T>
    {
        public T Value { get; }
        private readonly DateTimeOffset _created;

        public CacheEntry(T value)
        {
            Value = value;
            _created = DateTimeOffset.UtcNow;
        }

        public bool IsExpired(TimeSpan ttl) => DateTimeOffset.UtcNow - _created > ttl;
    }
}
