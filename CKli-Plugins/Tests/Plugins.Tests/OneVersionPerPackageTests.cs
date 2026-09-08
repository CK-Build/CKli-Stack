using CK.Core;
using CKli;
using CKli.Core;
using CKli.Publish.Plugin;
using NUnit.Framework;
using Shouldly;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A repository may reference a package identifier in ONE version only. The shallow read decides this
/// with every Condition ignored, so two conditional references across target frameworks are two versions
/// and are refused like any other pair. These tests use the fake build harness only.
/// </summary>
public class OneVersionPerPackageTests
{
    /// <summary>
    /// Two projects of one repository referencing the same external package in different versions: the
    /// solution cannot be read at all, so the command fails and says which files disagree.
    /// </summary>
    [Test]
    public async Task two_versions_of_one_package_in_a_repository_is_an_error_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddProject( "X.Other", branchName: "dev/stable" );
            e.AddOrUpdateReference( "X.Core", "Ext.Clash", SVersion.Parse( "1.0.0" ), "dev/stable" );
            e.AddOrUpdateReference( "X.Other", "Ext.Clash", SVersion.Parse( "2.0.0" ), "dev/stable" );
        }

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Package 'Ext.Clash' is referenced in two versions by this repository:" )
                                     && l.Contains( "'1.0.0' in 'X.Core/X.Core.csproj'" )
                                     && l.Contains( "'2.0.0' in 'X.Other/X.Other.csproj'" ) );
        }
    }

    /// <summary>
    /// The same identifier in the same version in two projects is the normal case and is accepted.
    /// </summary>
    [Test]
    public async Task the_same_version_in_two_projects_is_fine_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddProject( "X.Other", branchName: "dev/stable" );
            e.AddOrUpdateReference( "X.Core", "Ext.Shared", SVersion.Parse( "1.0.0" ), "dev/stable" );
            e.AddOrUpdateReference( "X.Other", "Ext.Shared", SVersion.Parse( "1.0.0" ), "dev/stable" );
        }

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        folder.Profiles.Single().DirectDependencies.Select( x => x.ToString() ).ShouldBe( ["Ext.Shared@1.0.0"] );
    }

    /// <summary>
    /// AddOrUpdateReference updates the existing reference rather than appending a second one. Before this
    /// was fixed the fake harness silently produced a duplicate item, which is exactly the state the rule
    /// above forbids - and it made the "misaligned sibling" arrange of SkippedRepositoryTests reference
    /// both the superseded version and the current one.
    /// </summary>
    [Test]
    public async Task updating_a_reference_replaces_its_version_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // A commit that targets a branch ref does not touch the working folder: read the branch tip.
        static ImmutableArray<PackageInstance> ReadReferences( FakeBuildRepo repo )
        {
            using var e = repo.CreateEditor();
            return e.ReadProjects( "dev/stable" ).Single( p => p.ProjectName == "X.Consumer" ).References;
        }

        ReadReferences( rConsumer ).Select( r => r.ToString() ).ShouldBe( ["X.Core@1.0.1"] );

        // The repository already references X.Core: this must move the version, not add a second element.
        rConsumer.AddOrUpdateReference( rCore, "v1.0.0", branchName: "dev/stable" );

        // A single reference, at the new version. Appending a second one - what this used to do - would
        // give both "X.Core@1.0.1" and "X.Core@1.0.0" here.
        ReadReferences( rConsumer ).Select( r => r.ToString() ).ShouldBe( ["X.Core@1.0.0"] );
    }
}
