using YukkuriMovieMaker.Plugin;

namespace YmmpxLibV2.Plugin;

/// <summary>
/// Registers the v2 YMM4 bridge when YMM4 loads this plugin.
/// </summary>
[PluginDetails(AuthorName = "hazimeyou", ContentId = "YmmpxLibV2")]
public sealed class YmmpxLibV2Plugin : IPlugin
{
    private static readonly Ymm4Bridge Bridge = new();

    public YmmpxLibV2Plugin()
    {
        YmmpxRuntime.RegisterYmmBridge(Bridge);
    }

    public string Name => "YmmpxLib v2 Integration";

    private sealed class Ymm4Bridge : IYmmBridge
    {
    }
}
