using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// &lt;Build DeleteBeforeBuild="..." /&gt;: content produced by a previous build must be deleted before the
/// next one instead of being reused. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>): the deletion happens
/// before the OnCoreBuild event, which is the hook the fake builder uses.
/// </summary>
public class DeleteBeforeBuildTests
{
    static void Configure( XElement plugins, string deleteBeforeBuild )
    {
        plugins.Element( "Build" ).ShouldNotBeNull().SetAttributeValue( "DeleteBeforeBuild", deleteBeforeBuild );
    }

    /// <summary>
    /// A "$StObjGen" folder sits at the root of every project that uses code generation: the entry has no '/'
    /// so it is matched at any depth. It is git ignored, so the build deletes it.
    /// </summary>
    [Test]
    public async Task ignored_generated_folders_are_deleted_before_the_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: ( m, p, plugins ) =>
        {
            Helper.ConfigureFakeFeeds( m, p, plugins );
            Configure( plugins, "$StObjGen" );
        } ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var repo = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );

        // Only git ignored content can be deleted. This commit is also the code change that gives the
        // build something to do.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, "dev/stable", fileContent: _ => "$StObjGen/", fileName: ".gitignore" );

        // What a previous run left behind: ignored, so it survives the checkout and the hard reset.
        var generated = repo.WorkingFolderPath.AppendPart( "$StObjGen" );
        Directory.CreateDirectory( generated );
        File.WriteAllText( generated.AppendPart( "G0.cs" ), "public class Stale {}" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "build" )).ShouldBeTrue();

        Directory.Exists( generated ).ShouldBeFalse();
    }

    /// <summary>
    /// A build must produce the artifacts of the commit that it tags. An entry that matches content which is
    /// not git ignored fails the build instead of silently building something else.
    /// </summary>
    [Test]
    public async Task deleting_non_ignored_content_fails_the_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: ( m, p, plugins ) =>
        {
            Helper.ConfigureFakeFeeds( m, p, plugins );
            Configure( plugins, "Tracked.cs" );
        } ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var repo = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, "dev/stable", fileContent: _ => "public class Tracked {}", fileName: "Tracked.cs" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "build" )).ShouldBeFalse();
            logs.ShouldContain( t => t.Contains( "is not git ignored" ) );
        }
        // The build failed before touching it.
        File.Exists( repo.WorkingFolderPath.AppendPart( "Tracked.cs" ) ).ShouldBeTrue();
    }
}
