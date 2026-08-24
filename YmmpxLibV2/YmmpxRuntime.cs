using System.Threading;

namespace YmmpxLibV2;

/// <summary>
/// Stores the optional YMM4 bridge registered by <c>YmmpxLibV2Plugin</c>.
/// </summary>
public static class YmmpxRuntime
{
    private static IYmmBridge? ymmBridge;

    /// <summary>
    /// Gets whether a YMM4 bridge has been registered.
    /// </summary>
    public static bool IsYmmBridgeAvailable => Volatile.Read(ref ymmBridge) is not null;

    /// <summary>
    /// Gets the registered YMM4 bridge, or <see langword="null"/> when the plugin is not loaded.
    /// </summary>
    public static IYmmBridge? GetYmmBridge() => Volatile.Read(ref ymmBridge);

    /// <summary>
    /// Registers the YMM4 bridge supplied by the plugin.
    /// </summary>
    /// <param name="bridge">The bridge instance supplied by <c>YmmpxLibV2Plugin</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bridge"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A different bridge is already registered.</exception>
    public static void RegisterYmmBridge(IYmmBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);

        var registeredBridge = Interlocked.CompareExchange(ref ymmBridge, bridge, null);
        if (registeredBridge is null || ReferenceEquals(registeredBridge, bridge))
            return;

        throw new InvalidOperationException("A different YMM bridge is already registered.");
    }

    internal static void ResetYmmBridgeForTesting() => Volatile.Write(ref ymmBridge, null);
}
