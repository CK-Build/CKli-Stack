using CK.Core;
using CK.Monitoring;
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
/// succeed: committing 'building/' versions to 'local/' ones"). When a LATER repository of the roadmap fails,
/// that promotion never runs and every earlier repository keeps the "building/vX" it just wrote.
/// </para>
/// <para>
/// That surviving "building/vX" is the useful half: it says where a build was attempted and did not complete.
/// What must NOT survive with it is the "local/vX" it was rolling - the two prefixes are two states of one
/// version tag, and a version is borne by a single commit. <c>ApplyReleaseBuildTag</c> drops the other state's
/// tag for that reason; before it did, this scenario left one version on two commits and the World no longer
/// loaded. See <c>CommitBuildInfo.ApplyReleaseBuildTag</c>.
/// </para>
/// </summary>
public class PartialBuildFailureTests
{
    /// <summary>
    /// A failing downstream build leaves the "building/" tag of the repositories that succeeded, and ONLY that:
    /// the pending "local/" release it superseded is gone, so the version is still borne by a single commit.
    /// </summary>
    [Test]
    public async Task a_failing_downstream_build_leaves_one_tag_per_version_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        // A first build succeeds: X-Core carries a pending "local/v1.0.2" release.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();
        VersionTags( rCore ).ShouldBe( ["local/v1.0.2", "v1.0.1"], ignoreOrder: true );
        var firstBuildCommit = CommitOf( rCore, "local/v1.0.2" );

        // New code on "stable", and the downstream build now fails. X-Core still rolls its pending release
        // forward - the version is unpublished, so the build targets v1.0.2 again, on the new commit.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "stable" );
        rConsumer.FailBuild = true;

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeFalse( "The roadmap failed." );

        // One version, one tag: the "local/v1.0.2" of the first build has been dropped by the "building/v1.0.2"
        // that took over, and the promotion that would have renamed it never ran.
        VersionTags( rCore ).ShouldBe( ["building/v1.0.2", "v1.0.1"], ignoreOrder: true );
        var failedBuildCommit = CommitOf( rCore, "building/v1.0.2" );
        failedBuildCommit.ShouldNotBe( firstBuildCommit );
    }


    /// <summary>
    /// The "building/vX" a failed roadmap leaves behind is promoted by the next roadmap that succeeds, even
    /// though that repository is not rebuilt: RoadmapExecutor's promotion loop walks ALL of OrderedSolutions,
    /// not only the ones it built. The interrupted build is completed in place, on its own commit.
    /// <para>
    /// A roadmap with NOTHING to build returns before that loop, so it used to leave such a tag untouched
    /// while still counting it as publishable. Roadmap.BuildAsync normalizes it on that path too - see
    /// <see cref="a_stranded_building_tag_is_normalized_when_nothing_must_be_built_Async"/>.
    /// </para>
    /// </summary>
    [Test]
    public async Task a_stranded_building_tag_is_promoted_by_the_next_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        rConsumer.FailBuild = true;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeFalse();
        VersionTags( rCore ).ShouldBe( ["building/v1.0.2", "v1.0.1"], ignoreOrder: true );
        var failedBuildCommit = CommitOf( rCore, "building/v1.0.2" );

        // The build can now succeed. It rebuilds X-Consumer - which never reached ApplyReleaseBuildTag - and
        // leaves X-Core alone: its "building/v1.0.2" reads as the version it already carries.
        rConsumer.FailBuild = false;
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "Required build for 1 repositories across the 2 repositories" );

        // Both are promoted: X-Consumer because it was just built, X-Core because the promotion loop covers
        // every ordered solution. Its version did not move - only the state of its tag did.
        VersionTags( rConsumer ).ShouldContain( "local/v0.1.2" );
        VersionTags( rCore ).ShouldBe( ["local/v1.0.2", "v1.0.1"], ignoreOrder: true );
        CommitOf( rCore, "local/v1.0.2" ).ShouldBe( failedBuildCommit, "Completed in place: the commit is unchanged." );
    }

    /// <summary>
    /// The other healing path: a roadmap that finds NOTHING to build returns before RoadmapExecutor runs, so
    /// its promotion loop is never reached. Roadmap.BuildAsync normalizes the interrupted "building/" tags
    /// itself on that path - otherwise the tag would survive every subsequent "nothing to build" run while
    /// still being counted as publishable.
    /// </summary>
    [Test]
    public async Task a_stranded_building_tag_is_normalized_when_nothing_must_be_built_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        // Both repositories reach v1.0.2/v0.1.2 first: this is what makes the LAST build have nothing to do.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();

        // A new commit and a failing downstream: X-Core is tagged "building/" and never promoted.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "stable" );
        rConsumer.FailBuild = true;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeFalse();
        VersionTags( rCore ).ShouldBe( ["building/v1.0.2", "v1.0.1"], ignoreOrder: true );
        var failedBuildCommit = CommitOf( rCore, "building/v1.0.2" );

        // Nothing changed since, so this build has nothing to do - and still repairs the tag.
        rConsumer.FailBuild = false;
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "There is nothing to build across the 2 repositories" );

        VersionTags( rCore ).ShouldBe( ["local/v1.0.2", "v1.0.1"], ignoreOrder: true );
        CommitOf( rCore, "local/v1.0.2" ).ShouldBe( failedBuildCommit, "Completed in place: the commit is unchanged." );
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

        // A GrandOutput memory collector, not TestHelper.Monitor.CollectTexts: the fake build runs on the
        // roadmap's own per solution monitor (builds go up to --max-dop), which a monitor scoped client never
        // sees. ExtractCurrentTexts() waits for the dispatcher (DispatcherSink.SyncWait) before returning.
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeFalse();
            var texts = logs.ExtractCurrentTexts();
            texts.ShouldContain( t => t.Contains( "Fake build failure requested by FakeBuildRepo.FailBuild" )
                                      && t.Contains( "X-Consumer" ) );
            texts.ShouldNotContain( t => t.Contains( "Fake build failure requested by FakeBuildRepo.FailBuild" )
                                         && t.Contains( "X-Core" ) );
        }
        // X-Core built and was tagged; X-Consumer failed before ApplyReleaseBuildTag, so it has no new tag.
        VersionTags( rCore ).ShouldBe( ["building/v1.0.2", "v1.0.1"], ignoreOrder: true );
        VersionTags( rConsumer ).ShouldBe( ["v0.1.1"] );


        // Cleared: the same roadmap now goes through.
        rConsumer.FailBuild = false;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();
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
