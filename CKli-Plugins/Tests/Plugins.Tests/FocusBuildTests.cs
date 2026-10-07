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
/// "ckli build --focus": the development loop of a developer working in a pivot while modifying its upstreams.
/// <para>
/// A plain "build" skips the upstreams of the pivots even when they have a code change (the CodeChange reason is
/// skippable), and "*build" builds everything that has a reason to build, the unrelated repositories included.
/// "--focus" is in between: the pivots' upstreams are in scope, the unrelated repositories are not.
/// </para>
/// </summary>
public class FocusBuildTests
{
    /// <summary>
    /// X-Up is modified. X-Pivot and X-Sibling consume it, X-Other is unrelated and modified too.
    /// <list type="bullet">
    ///   <item>"build" from X-Pivot: X-Up is skipped, so nothing is built.</item>
    ///   <item>"build --focus --continue": X-Up, X-Pivot and X-Sibling. X-Sibling is neither upstream nor downstream
    ///   of the pivot but a consumer of a rebuilt package is always rebuilt. X-Other is skipped.</item>
    ///   <item>"*build": the 4 of them.</item>
    /// </list>
    /// </summary>
    [Test]
    public async Task focus_builds_the_upstreams_of_the_pivots_and_skips_the_unrelated_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        var rSibling = await world.CreateRepoAsync( "X-Sibling", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        var rOther = await world.CreateRepoAsync( "X-Other", "v1.0.0" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );
        TestHelper.TouchAndCommit( rOther.WorkingFolderPath, branchName: null );

        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--dry-run" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "There is nothing to build from the single pivot out of 4 repositories" );

        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "*build", "--dry-run" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "Required build for 4 from the single pivot out of 4 repositories" );

        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--continue", "--dry-run" )).ShouldBeTrue();
        var screen = stack.Screen.ToString();
        screen.ShouldContain( "Required build for 3 from the single pivot out of 4 repositories" );
        screen.ShouldNotContain( "may detect required builds in upstreams repositories", customMessage: "'--focus' already considers the upstreams." );
        screen.ShouldNotContain( "'--focus' stops before" );

        var before = (Up: TagCount( rUp ), Pivot: TagCount( rPivot ), Sibling: TagCount( rSibling ), Other: TagCount( rOther ));
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--continue" )).ShouldBeTrue();
            // Only X-Other has its own reason to build and is left out: X-Sibling is built, X-Pivot is the pivot.
            logs.ExtractCurrentTexts().ShouldContain( "'X-Other' is out of focus and was not built. Run '*build' to complete the World." );
        }
        TagCount( rUp ).ShouldBe( before.Up + 1 );
        TagCount( rPivot ).ShouldBe( before.Pivot + 1 );
        TagCount( rSibling ).ShouldBe( before.Sibling + 1 );
        TagCount( rOther ).ShouldBe( before.Other, "X-Other is out of focus." );
    }

    /// <summary>
    /// Once X-Up is built, X-Pivot and X-Sibling are both ready and there are 2 monitors: without "--focus" they
    /// start together. With "--focus --continue", X-Sibling (neither upstream nor downstream of the pivot) waits for the pivot to
    /// complete: the pivots' results are really first.
    /// </summary>
    [Test]
    public async Task focus_holds_the_other_builds_until_the_pivots_are_built_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Sibling", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );

        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--continue", "--max-dop", "2" )).ShouldBeTrue();
            var texts = logs.ExtractCurrentTexts().ToList();
            int pivotDone = texts.IndexOf( "Build 'X-Pivot' succeed." );
            int siblingStart = texts.IndexOf( "Building roadmap n°3/3: 'X-Sibling'." );
            pivotDone.ShouldBeGreaterThanOrEqualTo( 0 );
            siblingStart.ShouldBeGreaterThan( pivotDone );
        }
    }

    /// <summary>
    /// By default, "--focus" stops once the pivots and their upstreams are built: X-Up and X-Pivot are built,
    /// X-Sibling (a consumer of X-Up that is not related to the pivot) is not started and the command succeeds.
    /// A following "build --focus --continue" builds X-Sibling.
    /// </summary>
    [Test]
    public async Task focus_stops_after_the_focused_builds_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        var rSibling = await world.CreateRepoAsync( "X-Sibling", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );

        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--dry-run" )).ShouldBeTrue();
        var screen = stack.Screen.ToString();
        screen.ShouldContain( "Required build for 3 from the single pivot out of 3 repositories" );
        screen.ShouldContain( "('--focus' stops before the other build: use '--continue' to build it.)" );

        var before = (Up: TagCount( rUp ), Pivot: TagCount( rPivot ), Sibling: TagCount( rSibling ));
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--max-dop", "2" )).ShouldBeTrue();
            var texts = logs.ExtractCurrentTexts();
            texts.ShouldContain( "Stopped after the focused builds ('--focus'): 1 of 3 builds were not started. Use '--continue' to build them." );
            texts.ShouldNotContain( "Building roadmap n°3/3: 'X-Sibling'." );
        }
        TagCount( rUp ).ShouldBe( before.Up + 1 );
        TagCount( rPivot ).ShouldBe( before.Pivot + 1 );
        TagCount( rSibling ).ShouldBe( before.Sibling, "X-Sibling is not started." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--continue" )).ShouldBeTrue();
        TagCount( rSibling ).ShouldBe( before.Sibling + 1 );
    }

    /// <summary>
    /// The development loop: X-Up is modified twice, each time followed by a "build --focus" that stops after the
    /// focused builds. Each run leaves the "building/" tags of the new CI versions of X-Up and X-Pivot: the roadmap
    /// did not finish its job, X-Sibling has not been built.
    /// <para>
    /// A CI build produces a new version each time: the "building/" tags don't pile up for all that, the second
    /// run replaces the ones of the first run. Once a build completes the roadmap, no "building/" tag remains.
    /// </para>
    /// </summary>
    [Test]
    public async Task successive_stopped_focus_builds_leave_no_building_tag_once_completed_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        var rSibling = await world.CreateRepoAsync( "X-Sibling", "v1.0.0", references: [rUp] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus" )).ShouldBeTrue();
        BuildingTags( rUp ).Length.ShouldBe( 1 );
        BuildingTags( rPivot ).Length.ShouldBe( 1 );
        var firstUp = BuildingTags( rUp )[0];

        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus" )).ShouldBeTrue();
        // The second run built a new CI version of X-Up and its "building/" tag replaced the one of the first run.
        firstUp.ShouldBe( "building/v1.0.1--ci.1" );
        VersionTags( rUp ).ShouldBe( ["building/v1.0.1--ci.2", "v1.0.0"], ignoreOrder: true );
        BuildingTags( rPivot ).Length.ShouldBe( 1 );

        // Completes the roadmap: X-Sibling is built.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--continue" )).ShouldBeTrue();
        VersionTags( rSibling ).ShouldContain( t => t.StartsWith( "local/" ) );

        foreach( var r in new[] { rUp, rPivot, rSibling } )
        {
            BuildingTags( r ).ShouldBeEmpty( $"No 'building/' tag must remain in '{r.DisplayPath}'. Its tags: '{string.Join( "', '", VersionTags( r ) )}'." );
        }
    }

    /// <summary>
    /// "--continue" only applies to "--focus".
    /// </summary>
    [Test]
    public async Task continue_requires_focus_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0" ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--continue", "--dry-run" )).ShouldBeFalse();
            logs.ShouldContain( "'--continue' applies to '--focus' that must be specified." );
        }
    }

    /// <summary>
    /// X-Pivot fails. With "--focus", X-Sibling is held until X-Pivot completes, and the failure stops the build:
    /// X-Sibling never starts. A "*build" - same roadmap, no "--focus" - goes on and builds it.
    /// </summary>
    [TestCase( true )]
    [TestCase( false )]
    public async Task focus_stops_at_the_first_failure_Async( bool focus )
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        var rSibling = await world.CreateRepoAsync( "X-Sibling", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );
        rPivot.FailBuild = true;

        int siblingBefore = TagCount( rSibling );
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            string[] args = focus ? ["build", "--focus", "--max-dop", "1"] : ["*build", "--max-dop", "1"];
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, args )).ShouldBeFalse();
            var texts = logs.ExtractCurrentTexts();
            if( focus )
            {
                texts.ShouldContain( "Stopped after the first failure ('--focus'): 1 of 3 builds were not started." );
                TagCount( rSibling ).ShouldBe( siblingBefore, "X-Sibling never started." );
            }
            else
            {
                texts.ShouldNotContain( t => t.StartsWith( "Stopped after the first failure" ) );
                TagCount( rSibling ).ShouldBe( siblingBefore + 1, "Without '--focus', X-Sibling is built." );
            }
        }
    }

    /// <summary>
    /// A non-CI build reads only the pivots from their "dev/" branch: the work in progress of an upstream would be
    /// invisible to "--focus", so the two flags are exclusive.
    /// </summary>
    [Test]
    public async Task focus_and_release_are_exclusive_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0" ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--regular", "--dry-run" )).ShouldBeFalse();
            logs.ShouldContain( "'--regular' and '--focus' are exclusive: '--focus' needs the \"dev/\" branches of the upstreams that only a CI build considers." );
        }
    }

    /// <summary>
    /// Without pivots (from the World root, or with "--all") every repository is in scope: "--focus" is ignored
    /// with a warning, and the roadmap is the one of a plain "build".
    /// </summary>
    [Test]
    public async Task focus_without_pivots_is_ignored_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Pivot", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );

        stack.Screen.Clear();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--focus", "--dry-run" )).ShouldBeTrue();
            logs.ShouldContain( "'--focus' is ignored: all the repositories are selected, there is no pivot to focus on." );
        }
        stack.Screen.ToString().ShouldContain( "Required build for 2 repositories across the 2 repositories" );
    }

    static string[] VersionTags( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                .Select( t => t.FriendlyName )
                .Where( n => n.StartsWith( 'v' ) || n.Contains( "/v" ) )
                .ToArray();
    }

    static string[] BuildingTags( FakeBuildRepo repo ) => VersionTags( repo ).Where( t => t.StartsWith( "building/" ) ).ToArray();

    static int TagCount( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags.Count();
    }
}
