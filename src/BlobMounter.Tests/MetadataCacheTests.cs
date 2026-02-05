using BlobMounter.Core.Azure;
using BlobMounter.Core.Models;

namespace BlobMounter.Tests;

public class MetadataCacheTests
{
    private static BlobItemInfo CreateItem(string path, bool isDir = false) => new()
    {
        Name = PathMapper.GetName(path),
        FullPath = path,
        Size = 100,
        LastModified = DateTimeOffset.UtcNow,
        IsDirectory = isDir,
    };

    [Fact]
    public void GetItem_ReturnsNull_WhenNotCached()
    {
        var cache = new MetadataCache();
        Assert.Null(cache.GetItem("some/path.txt"));
    }

    [Fact]
    public void SetItem_ThenGetItem_ReturnsCachedValue()
    {
        var cache = new MetadataCache();
        var item = CreateItem("folder/file.txt");
        cache.SetItem("folder/file.txt", item);

        var result = cache.GetItem("folder/file.txt");
        Assert.NotNull(result);
        Assert.Equal("file.txt", result.Name);
    }

    [Fact]
    public void GetItem_ReturnsNull_AfterTtlExpires()
    {
        var cache = new MetadataCache(ttl: TimeSpan.FromMilliseconds(50));
        var item = CreateItem("folder/file.txt");
        cache.SetItem("folder/file.txt", item);

        Thread.Sleep(100);

        Assert.Null(cache.GetItem("folder/file.txt"));
    }

    [Fact]
    public void SetListing_ThenGetListing_ReturnsCachedList()
    {
        var cache = new MetadataCache();
        var items = new List<BlobItemInfo> { CreateItem("folder/a.txt"), CreateItem("folder/b.txt") };
        cache.SetListing("folder/", items);

        var result = cache.GetListing("folder/");
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void InvalidateItem_RemovesCachedItem()
    {
        var cache = new MetadataCache();
        cache.SetItem("folder/file.txt", CreateItem("folder/file.txt"));
        cache.InvalidateItem("folder/file.txt");

        Assert.Null(cache.GetItem("folder/file.txt"));
    }

    [Fact]
    public void InvalidatePrefix_RemovesMatchingListings()
    {
        var cache = new MetadataCache();
        cache.SetListing("folder/", new List<BlobItemInfo> { CreateItem("folder/a.txt") });
        cache.SetListing("other/", new List<BlobItemInfo> { CreateItem("other/b.txt") });

        cache.InvalidatePrefix("folder/");

        Assert.Null(cache.GetListing("folder/"));
        Assert.NotNull(cache.GetListing("other/"));
    }

    [Fact]
    public void InvalidatePrefix_AlsoRemovesItemsUnderPrefix()
    {
        var cache = new MetadataCache();
        cache.SetItem("folder/file.txt", CreateItem("folder/file.txt"));
        cache.SetItem("other/file.txt", CreateItem("other/file.txt"));

        cache.InvalidatePrefix("folder/");

        Assert.Null(cache.GetItem("folder/file.txt"));
        Assert.NotNull(cache.GetItem("other/file.txt"));
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var cache = new MetadataCache();
        cache.SetItem("a.txt", CreateItem("a.txt"));
        cache.SetListing("folder/", new List<BlobItemInfo> { CreateItem("folder/b.txt") });

        cache.Clear();

        Assert.Null(cache.GetItem("a.txt"));
        Assert.Null(cache.GetListing("folder/"));
    }
}
