using BlobMounter.Core.Azure;

namespace BlobMounter.Tests;

public class PathMapperTests
{
    [Fact]
    public void ToBlobPath_NoPrefix_ConvertsBackslashesToForwardSlashes()
    {
        var mapper = new PathMapper(null);
        Assert.Equal("folder/file.txt", mapper.ToBlobPath(@"\folder\file.txt"));
    }

    [Fact]
    public void ToBlobPath_WithPrefix_PrependsPrefix()
    {
        var mapper = new PathMapper("data/files");
        Assert.Equal("data/files/folder/file.txt", mapper.ToBlobPath(@"\folder\file.txt"));
    }

    [Fact]
    public void ToBlobPath_RootPath_ReturnsEmptyOrPrefix()
    {
        var mapper = new PathMapper(null);
        Assert.Equal("", mapper.ToBlobPath(@"\"));
    }

    [Fact]
    public void ToBlobPath_RootPath_WithPrefix_ReturnsPrefix()
    {
        var mapper = new PathMapper("myprefix");
        Assert.Equal("myprefix/", mapper.ToBlobPath(@"\"));
    }

    [Fact]
    public void ToWindowsPath_NoPrefix_ConvertsForwardSlashesToBackslashes()
    {
        var mapper = new PathMapper(null);
        Assert.Equal(@"\folder\file.txt", mapper.ToWindowsPath("folder/file.txt"));
    }

    [Fact]
    public void ToWindowsPath_WithPrefix_StripsPrefix()
    {
        var mapper = new PathMapper("data/files");
        Assert.Equal(@"\folder\file.txt", mapper.ToWindowsPath("data/files/folder/file.txt"));
    }

    [Fact]
    public void GetListPrefix_RootNoPrefix_ReturnsEmpty()
    {
        var mapper = new PathMapper(null);
        Assert.Equal("", mapper.GetListPrefix(@"\"));
    }

    [Fact]
    public void GetListPrefix_RootWithPrefix_ReturnsPrefixWithSlash()
    {
        var mapper = new PathMapper("mydata");
        Assert.Equal("mydata/", mapper.GetListPrefix(@"\"));
    }

    [Fact]
    public void GetListPrefix_SubdirectoryNoPrefix_ReturnsDirectoryWithSlash()
    {
        var mapper = new PathMapper(null);
        Assert.Equal("folder/", mapper.GetListPrefix(@"\folder"));
    }

    [Fact]
    public void GetListPrefix_SubdirectoryWithPrefix_ReturnsCombinedPath()
    {
        var mapper = new PathMapper("root");
        Assert.Equal("root/folder/sub/", mapper.GetListPrefix(@"\folder\sub"));
    }

    [Fact]
    public void GetName_SimpleFile_ReturnsFileName()
    {
        Assert.Equal("file.txt", PathMapper.GetName("folder/file.txt"));
    }

    [Fact]
    public void GetName_DirectoryPrefix_ReturnsFolderName()
    {
        Assert.Equal("folder", PathMapper.GetName("folder/"));
    }

    [Fact]
    public void GetName_DeepPath_ReturnsLastSegment()
    {
        Assert.Equal("deep.log", PathMapper.GetName("a/b/c/deep.log"));
    }

    [Fact]
    public void GetName_NoSlashes_ReturnsWholeName()
    {
        Assert.Equal("file.txt", PathMapper.GetName("file.txt"));
    }

    [Fact]
    public void PrefixWithTrailingSlash_IsNormalized()
    {
        var mapper = new PathMapper("data/");
        Assert.Equal("data/file.txt", mapper.ToBlobPath(@"\file.txt"));
    }

    [Fact]
    public void PrefixWithBackslashes_IsNormalized()
    {
        var mapper = new PathMapper(@"data\sub");
        Assert.Equal("data/sub/file.txt", mapper.ToBlobPath(@"\file.txt"));
    }
}
