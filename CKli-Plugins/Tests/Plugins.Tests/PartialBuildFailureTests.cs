using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// What a partially failed build leaves behind.
/// <para>
/// A build writes a "building/vX" tag on each solution it builds and promotes them all to "local/vX" only once
/// the WHOLE roadmap succeeded (<c>BuildPlugin.RoadmapExecutor</c>: <c>if( result != null )</c> ... "Build
/// succeed: committing 'building/' versions to 'local/' ones"). That promotion is also what absorbs a pending
/// "local/vX" of the same version, through <c>BuildResult.CommitBuilding</c>'s <c>allowOverwrite: true</c> -
/// <c>ApplyReleaseBuildTag</c> deliberately leaves it alone, since its <c>DestroyLocalReleases</c> filter is
/// <c>v != _version</c> and <see cref="SVersion"/> equality ignores the "local/"/"building/" prefix.
/// </para>
/// <para>
/// So when a LATER repository of the roadmap fails, an earlier one keeps its "building/vX" beside the stale
/// "local/vX" it was rolling: one version, two tags, two commits.
/// </para>
/// </summary>
public class PartialBuildFailureTests
{
    /// <summary>
    /// CHARACTERIZATION TEST - it pins a BUG, not a specification.
    /// <para>
    /// The asserted end state is wrong: "local/v1.0.2" and "building/v1.0.2" are the same version (equality
    /// ignores the prefix) on two different commits. A fix must make this test change - either by rolling the
    /// "building/" tags back when the build fails, or by having ApplyReleaseBuildTag move the same-version
    /// "local/" tag instead of leaving it.
    /// </para>
    /// </summary>
    [Test]
    public async Task a_failing_downstream_build_leaves_two_tags_for_one_version_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        // A first build succeeds: X-Core carries a pending "local/v1.0.2" release.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        VersionTags( rCore ).ShouldBe( ["local/v1.0.2", "v1.0.1"], ignoreOrder: true );
        var firstBuildCommit = CommitOf( rCore, "local/v1.0.2" );

        // New code on "stable", and the downstream build now fails. X-Core still rolls its pending release
        // forward - the version is unpublished, so the build targets v1.0.2 again, on the new commit.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "stable" );
        rConsumer.FailBuild = true;

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeFalse( "The roadmap failed." );

        // BUG: one version, two tags, two commits. "building/v1.0.2" was written on the new commit and the
        // promotion that would have absorbed "local/v1.0.2" never ran.
        VersionTags( rCore ).ShouldBe( ["building/v1.0.2", "local/v1.0.2", "v1.0.1"], ignoreOrder: true );
        CommitOf( rCore, "local/v1.0.2" ).ShouldBe( firstBuildCommit );
        CommitOf( rCore, "building/v1.0.2" ).ShouldNotBe( firstBuildCommit );
    }

    /// <summary>
    /// The harness seam itself: FakeBuildRepo.FailBuild fails only the repository that declares it, and is
    /// settable back to false.
    /// </summary>
    [Test]
    public async Task FailBuild_targets_one_repository_and_can_be_cleared_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        rConsumer.FailBuild = true;

        // CollectAllTexts, not TestHelper.Monitor.CollectTexts: the fake build runs on the roadmap's own per
        // solution monitor (builds go up to --max-dop), which a monitor scoped client never sees.
        using( var logs = TestHelper.CollectAllTexts() )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeFalse();
            logs.Texts.ShouldContain( t => t.Contains( "Fake build failure requested by FakeBuildRepo.FailBuild" )
                                           && t.Contains( "X-Consumer" ) );
            logs.Texts.ShouldNotContain( t => t.Contains( "Fake build failure requested by FakeBuildRepo.FailBuild" )
                                              && t.Contains( "X-Core" ) );
        }
        // X-Core built and was tagged; X-Consumer failed before ApplyReleaseBuildTag, so it has no new tag.
        VersionTags( rCore ).ShouldBe( ["building/v1.0.2", "v1.0.1"], ignoreOrder: true );
        VersionTags( rConsumer ).ShouldBe( ["v0.1.1"] );

        // Cleared: the same roadmap now goes through.
        rConsumer.FailBuild = false;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        VersionTags( rConsumer ).ShouldContain( "local/v0.1.2" );
    }

    static string[] VersionTags( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                .Select( t => t.FriendlyName )
                .Where( n => n.StartsWith( 'v' ) || n.Contains( "/v" ) )
                .ToArray();
    }

    static string CommitOf( FakeBuildRepo repo, string tagName )
    {
        using var e = repo.CreateEditor();
        var t = e.GitRepository.Repository.Tags[tagName];
        t.ShouldNotBeNull( $"Tag '{tagName}' not found in '{repo.DisplayPath}'." );
        return t.PeeledTarget.Sha;
    }
}
