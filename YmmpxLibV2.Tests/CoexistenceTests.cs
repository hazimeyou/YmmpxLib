using System.Reflection;
using YmmpxLib;
using Xunit;

namespace YmmpxLibV2.Tests;

public sealed class CoexistenceTests
{
    [Fact]
    public void V1AndV2CoreAssembliesHaveDistinctIdentities()
    {
        Assert.NotEqual(typeof(YmmpxLibraryInfo).Assembly.GetName().Name, typeof(YmmpxRuntime).Assembly.GetName().Name);
        Assert.Equal("YmmpxLib", typeof(YmmpxLibraryInfo).Assembly.GetName().Name);
        Assert.Equal("YmmpxLibV2", typeof(YmmpxRuntime).Assembly.GetName().Name);
    }

    [Fact]
    public void V1AndV2PluginAssembliesHaveDistinctIdentities()
    {
        var v1PluginName = AssemblyName.GetAssemblyName(GetPluginAssemblyPath("YMMPXLibPlugin", "net10.0-windows10.0.19041.0", "YmmpxLibPlugin.dll")).Name;
        var v2PluginName = AssemblyName.GetAssemblyName(GetPluginAssemblyPath("YmmpxLibV2Plugin", "net10.0-windows10.0.19041.0", "YmmpxLibV2Plugin.dll")).Name;

        Assert.NotEqual(v1PluginName, v2PluginName);
        Assert.Equal("YmmpxLibPlugin", v1PluginName);
        Assert.Equal("YmmpxLibV2Plugin", v2PluginName);
    }

    [Fact]
    public void V2CoreDoesNotReferenceThePluginOrYmm4()
    {
        var references = typeof(YmmpxRuntime).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("YmmpxLibV2Plugin", references);
        Assert.DoesNotContain("YukkuriMovieMaker", references);
        Assert.DoesNotContain("YukkuriMovieMaker.Plugin", references);
    }

    [Fact]
    public void V2PluginReferencesTheV2Core()
    {
        var pluginAssembly = Assembly.LoadFile(GetPluginAssemblyPath("YmmpxLibV2Plugin", "net10.0-windows10.0.19041.0", "YmmpxLibV2Plugin.dll"));
        var references = pluginAssembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.Contains("YmmpxLibV2", references);
    }

    private static string GetPluginAssemblyPath(string projectDirectory, string targetFramework, string assemblyFileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            projectDirectory,
            "bin", "Release", targetFramework, assemblyFileName));
    }
}
