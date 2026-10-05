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
    ///   <item>"build --focus": X-Up, X-Pivot and X-Sibling. X-Sibling is neither upstream nor downstream of the
    ///   pivot but a consumer of a rebuilt package is always rebuilt. X-Other is skipped.</item>
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--dry-run" )).ShouldBeTrue();
        var screen = stack.Screen.ToString();
        screen.ShouldContain( "Required build for 3 from the single pivot out of 4 repositories" );
        screen.ShouldNotContain( "may detect required builds in upstreams repositories", customMessage: "'--focus' already considers the upstreams." );

        var before = (Up: TagCount( rUp ), Pivot: TagCount( rPivot ), Sibling: TagCount( rSibling ), Other: TagCount( rOther ));
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus" )).ShouldBeTrue();
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
    /// start together. With it, X-Sibling (neither upstream nor downstream of the pivot) waits for the pivot to
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
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--focus", "--max-dop", "2" )).ShouldBeTrue();
            var texts = logs.ExtractCurrentTexts().ToList();
            int pivotDone = texts.IndexOf( "Build 'X-Pivot' succeed." );
            int siblingStart = texts.IndexOf( "Building roadmap n°3/3: 'X-Sibling'." );
            pivotDone.ShouldBeGreaterThanOrEqualTo( 0 );
            siblingStart.ShouldBeGreaterThan( pivotDone );
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

    static int TagCount( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags.Count();
    }
}
