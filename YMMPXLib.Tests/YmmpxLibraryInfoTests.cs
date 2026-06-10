using YmmpxLib;
using Xunit;

namespace YMMPXLib.Tests;

public sealed class YmmpxLibraryInfoTests
{
    [Fact]
    public void ApiVersion_IsDefined()
    {
        Assert.False(string.IsNullOrWhiteSpace(YmmpxLibraryInfo.ApiVersion));
        Assert.True(YmmpxLibraryInfo.ApiVersionCode > 0);
    }

    [Fact]
    public void InternalVersion_IsDefined()
    {
        Assert.False(string.IsNullOrWhiteSpace(YmmpxLibraryInfo.InternalVersion));
        Assert.True(YmmpxLibraryInfo.InternalVersionCode > 0);
    }
}
