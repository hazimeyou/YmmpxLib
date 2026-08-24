using YmmpxLibV2;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class YmmpxRuntimeTests : IDisposable
{
    public YmmpxRuntimeTests()
    {
        YmmpxRuntime.ResetYmmBridgeForTesting();
    }

    public void Dispose()
    {
        YmmpxRuntime.ResetYmmBridgeForTesting();
    }

    [Fact]
    public void BridgeIsUnavailableBeforePluginRegistersIt()
    {
        Assert.False(YmmpxRuntime.IsYmmBridgeAvailable);
        Assert.Null(YmmpxRuntime.GetYmmBridge());
    }

    [Fact]
    public void RegisterYmmBridgeMakesTheSameBridgeAvailable()
    {
        var bridge = new TestBridge();

        YmmpxRuntime.RegisterYmmBridge(bridge);

        Assert.True(YmmpxRuntime.IsYmmBridgeAvailable);
        Assert.Same(bridge, YmmpxRuntime.GetYmmBridge());
    }

    [Fact]
    public void RegisteringTheSameBridgeAgainIsANoOp()
    {
        var bridge = new TestBridge();
        YmmpxRuntime.RegisterYmmBridge(bridge);

        YmmpxRuntime.RegisterYmmBridge(bridge);

        Assert.Same(bridge, YmmpxRuntime.GetYmmBridge());
    }

    [Fact]
    public void RegisteringADifferentBridgeIsRejected()
    {
        YmmpxRuntime.RegisterYmmBridge(new TestBridge());

        var exception = Assert.Throws<InvalidOperationException>(() => YmmpxRuntime.RegisterYmmBridge(new TestBridge()));

        Assert.Equal("A different YMM bridge is already registered.", exception.Message);
    }

    private sealed class TestBridge : IYmmBridge
    {
    }
}
