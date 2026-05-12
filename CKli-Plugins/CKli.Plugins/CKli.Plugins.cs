using CKli.Core;

namespace CKli.Plugins;

public static class Plugins
{
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
            typeof(CKli.ReleaseDatabase.Plugin.ReleaseDatabasePlugin),
            typeof(CKli.ShallowSolution.Plugin.ShallowSolutionPlugin),
            typeof(CKli.VersionTag.Plugin.VersionTagInfo)
            // </AutoSection>
        ] );
    }
}
