using CKli.Core;

namespace CKli.Plugins;

/// <summary>
/// Main adapter between CKli.Plugins.Core and the plugins.
/// </summary>
public static class Plugins
{
    /// <summary>
    /// Called by CKli.Plugins.Loader when no <c>static CKli.Plugins.CompiledPlugins.Get( PluginCollectorContext ctx )</c>
    /// method exists or it returned null because <see cref="PluginCollectorContext.Signature"/> has changed.
    /// </summary>
    /// <param name="ctx">The collector context.</param>
    /// <returns>The reflection based plugin factory.</returns>
    public static IPluginFactory Register( PluginCollectorContext ctx )
    {
        return PluginCollector.Create( ctx ).BuildPluginFactory( [
            // <AutoSection>
            typeof(CKli.ArtifactHandler.Plugin.ArtifactHandlerPlugin),
            typeof(CKli.BranchModel.Plugin.BranchModelPlugin),
            typeof(CKli.Build.Plugin.BuildPlugin),
            typeof(CKli.CommonFiles.Plugin.CommonFilesPlugin),
            typeof(CKli.HotZone.Plugin.HotZonePlugin),
            typeof(CKli.Migration.Plugin.MigrationPlugin),
            typeof(CKli.Publish.Plugin.PublishPlugin),
            typeof(CKli.ShallowSolution.Plugin.ShallowSolutionPlugin),
            typeof(CKli.VersionTag.Plugin.VersionTagInfo)
            // </AutoSection>
        ] );
    }
}
