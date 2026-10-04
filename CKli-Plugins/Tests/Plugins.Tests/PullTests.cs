using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli pull" merges every tracked branch. A merge that conflicts is handled last: the merge assistant (HotZone)
/// aligns the package versions like "ckli branch sync" does, and a conflict that remains is left in progress only on
/// the branch named by "--branch" (its "dev/" branch when it exists). On any other branch it is not merged.
/// </summary>
public class PullTests
{
    [Test]
    public async Task a_real_conflict_is_left_in_progress_only_on_the_branch_named_by_branch_option_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (bob, tim, bobDisplay, _, _) = await ArrangeBobAndTimAsync( testEnv ).ConfigureAwait( false );

        await TouchDevStableAsync( bob.ChangeDirectory( "X-Core" ), "Same.txt", "Bob" ).ConfigureAwait( false );
        await TouchDevStableAsync( tim.ChangeDirectory( "X-Core" ), "Same.txt", "Tim" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim.ChangeDirectory( "X-Core" ), "branch", "push", "dev/stable" )).ShouldBeTrue();

        var bobCore = bob.CurrentDirectory.AppendPart( "X-Core" );
        var bobTip = DevStableTip( bobCore );

        // Without --branch, the conflict is reported and nothing is merged.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull", "--all" )).ShouldBeFalse();
        bobDisplay.ToString().ShouldContain( "A merge conflicts and is not merged: 'ckli pull --branch <name>' leaves the merge of the branch you work on in progress." );
        bobDisplay.ToString().ShouldContain( "Same.txt" );
        DevStableTip( bobCore ).ShouldBe( bobTip );
        IsMerging( bobCore ).ShouldBeFalse();

        // A dry run predicts it.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull", "--all", "--dry-run", "--branch", "stable" )).ShouldBeFalse();
        bobDisplay.ToString().ShouldContain( "Dry run:" );
        bobDisplay.ToString().ShouldContain( "A merge would be left in progress:" );
        IsMerging( bobCore ).ShouldBeFalse();

        // With --branch stable, the merge is left in progress on "dev/stable".
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull", "--all", "--branch", "stable" )).ShouldBeFalse();
        bobDisplay.ToString().ShouldContain( "A merge is left in progress: resolve its conflicts and commit it (or abort it)." );
        IsMerging( bobCore ).ShouldBeTrue();
        using( var git = new LibGit2Sharp.Repository( bobCore ) )
        {
            git.Head.FriendlyName.ShouldBe( "dev/stable" );
            git.Index.Conflicts.Select( c => c.Ours.Path ).ShouldBe( ["Same.txt"] );
        }
    }

    [Test]
    public async Task package_versions_that_conflict_are_aligned_and_merged_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (bob, tim, _, core, app) = await ArrangeBobAndTimAsync( testEnv ).ConfigureAwait( false );

        // Tim publishes X-Core (a CI build) and X-App's "dev/stable" now references it, while Bob has changed the
        // very same line of his own "dev/stable": each side references a different X-Core version.
        await TouchDevStableAsync( tim.ChangeDirectory( "X-Core" ), "Tim.txt", "Tim" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "publish" )).ShouldBeTrue();
        await CKliCommands.ExecAsync( TestHelper.Monitor, bob.ChangeDirectory( "X-App" ), "exec", "git", "branch", "dev/stable" );
        app.AddOrUpdateReference( core, "1.0.0", "dev/stable" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull", "--all" )).ShouldBeTrue();
            logs.ShouldContain( l => l.StartsWith( "Merging into 'dev/stable' in 'X-App' aligned 1 package version(s):" ) );
        }
        var bobApp = bob.CurrentDirectory.AppendPart( "X-App" );
        using( var git = new LibGit2Sharp.Repository( bobApp ) )
        {
            var tip = git.Branches["dev/stable"].Tip;
            tip.Parents.Count().ShouldBe( 2, "A merge commit." );
            tip.Parents.ShouldContain( git.Branches["origin/dev/stable"].Tip );
        }
        IsMerging( bobApp ).ShouldBeFalse();
    }

    // Bob creates X-Core and X-App (that consumes X-Core) and publishes them; Tim clones the remotes.
    static async Task<(CKliEnv Bob, CKliEnv Tim, StringScreen BobDisplay, FakeBuildRepo Core, FakeBuildRepo App)> ArrangeBobAndTimAsync( FakeBuildTestEnv testEnv )
    {
        var bobStack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = bobStack.DefaultWorld;
        var bob = world.WorldRoot;
        var core = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var app = await world.CreateRepoAsync( "X-App", "v1.0.0", references: [core] ).ConfigureAwait( false );
        await TouchDevStableAsync( bob.ChangeDirectory( "X-Core" ), "Init.txt", "Init" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish", "--release" )).ShouldBeTrue();

        var tim = await bobStack.Remotes.CloneAsync( testEnv.Path.AppendPart( "Tim" ),
                                                    allowDuplicateStack: true,
                                                    ( monitor, stackPath, plugins )
                                                        => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                                       .ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "branch", "switch", "stable" )).ShouldBeTrue();
        // The harness commits Tim's <Plugins> configuration in his Stack: both Stacks are synchronized.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "push", "--stack-only" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull", "--all" )).ShouldBeTrue();
        return (bob, tim, bobStack.Screen, core, app);
    }

    static async Task TouchDevStableAsync( CKliEnv context, string fileName, string content )
    {
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( context.CurrentDirectory, branchName: "dev/stable", fileContent: _ => content, fileName: fileName );
    }

    static string DevStableTip( NormalizedPath repo )
    {
        using var git = new LibGit2Sharp.Repository( repo );
        return git.Branches["dev/stable"].Tip.Sha;
    }

    static bool IsMerging( NormalizedPath repo ) => File.Exists( Path.Combine( repo, ".git", "MERGE_HEAD" ) );
}
