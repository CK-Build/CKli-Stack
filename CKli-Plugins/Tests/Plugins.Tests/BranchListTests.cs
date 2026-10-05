using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli branch list" is the only consumer of <see cref="CKli.BranchModel.Plugin.BranchNamespace.GetDisplayBranches"/>:
/// the compact link codes exist for this display and for nothing else - the configuration and the "--link"
/// option of "ckli branch open" both spell a link type by its name.
/// <para>
/// The second column summarizes, for each branch, the repositories where it has changes: the tips (no upstream
/// with changes), the number of repositories where the branch is opened without changes of its own and the
/// weight of the branch: the repositories and projects a build touches (the ones with changes and all their
/// downstreams, unchanged ones included since they are updated with their upstream's new version).
/// </para>
/// </summary>
public class BranchListTests
{
    [Test]
    public async Task branch_list_displays_the_opened_branches_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldContain( "stable" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "delta", "--link", "Manual" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Opened branches of 'Test':
            Branch        Repositories           
            stable        1 repository           
              => romeo    No change, 1 unchanged.
                |✋ delta  No change, 1 unchanged.

            Links:
              |✋ Manual: nothing is propagated from the parent.
              |> Regular: a version built on the parent is merged.
              -> CI (the default): any commit built on the parent is merged.
              => Full: every commit of the parent's "dev/" branch is merged.
            ❰✓❱

            """ );
    }

    /// <summary>
    /// X-Core &lt;- X-Middle &lt;- X-App and a standalone X-Other: "alpha" has changes in X-Core and X-App only.
    /// X-App is not a tip even if X-Middle (that has no changes) sits in between: upstreams are transitive.
    /// </summary>
    [Test]
    public async Task branch_list_displays_tips_unchanged_count_and_weight_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var core = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var middle = await world.CreateRepoAsync( "X-Middle", "v1.0.0", default, core ).ConfigureAwait( false );
        var app = await world.CreateRepoAsync( "X-App", "v1.0.0", default, middle ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Other", "v1.0.0" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "alpha", "--link", "Full" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( core.WorkingFolderPath, branchName: null );
        TestHelper.TouchAndCommit( app.WorkingFolderPath, branchName: null );
        // Opened everywhere, changes nowhere. It becomes the parent of "alpha", that keeps its changes.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "bravo", "--link", "Full" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Opened branches of 'Test':
            Branch        Repositories                                                                   
            stable        4 repositories                                                                 
              => bravo    No change, 4 unchanged.                                                        
                   ↖ 2 fast-forwards                                                                     
                => alpha  X-Core and 1 other repository, 2 unchanged, weight: 3 repositories, 3 projects.

            Links:
              |✋ Manual: nothing is propagated from the parent.
              |> Regular: a version built on the parent is merged.
              -> CI (the default): any commit built on the parent is merged.
              => Full: every commit of the parent's "dev/" branch is merged.

            Merges:
              ↖ "ckli branch close": what closing the branch would merge into its parent.
              ↘ "ckli branch sync": what synchronizing the branch would merge into it.
            ❰✓❱

            """ );
    }

    /// <summary>
    /// Above a branch, "↘" tells what "ckli branch sync" would do to it from its link: here a "Full" link in four
    /// repositories, one per outcome (the up to date one says nothing). "↖" tells what "ckli branch close" would merge
    /// into the parent. Once synchronized, only the conflicts are left: the lines predict what the commands do.
    /// </summary>
    [Test]
    public async Task branch_list_displays_what_branch_sync_would_do_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var ff = await world.CreateRepoAsync( "X-FastForward", "v1.0.0" ).ConfigureAwait( false );
        var merge = await world.CreateRepoAsync( "X-Merge", "v1.0.0" ).ConfigureAwait( false );
        var conflict = await world.CreateRepoAsync( "X-Conflict", "v1.0.0" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-UpToDate", "v1.0.0" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();

        TestHelper.TouchAndCommit( ff.WorkingFolderPath, branchName: "dev/stable" );
        TestHelper.TouchAndCommit( merge.WorkingFolderPath, branchName: "dev/sierra", fileName: "Sierra.txt" );
        TestHelper.TouchAndCommit( merge.WorkingFolderPath, branchName: "dev/stable" );
        TestHelper.TouchAndCommit( conflict.WorkingFolderPath, branchName: "dev/sierra", fileContent: _ => "sierra", fileName: "Conflict.txt" );
        TestHelper.TouchAndCommit( conflict.WorkingFolderPath, branchName: "dev/stable", fileContent: _ => "stable", fileName: "Conflict.txt" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Opened branches of 'Test':
            Branch       Repositories                                                         
            stable       4 repositories                                                       
                 ↖ 1 merge, 1 conflict (X-Conflict)                                           
                 ↘ 1 fast-forward, 1 merge, 1 conflict (X-Conflict)                           
              => sierra  X-Merge, X-Conflict, 2 unchanged, weight: 2 repositories, 2 projects.

            Links:
              |✋ Manual: nothing is propagated from the parent.
              |> Regular: a version built on the parent is merged.
              -> CI (the default): any commit built on the parent is merged.
              => Full: every commit of the parent's "dev/" branch is merged.

            Merges:
              ↖ "ckli branch close": what closing the branch would merge into its parent.
              ↘ "ckli branch sync": what synchronizing the branch would merge into it.
            ❰✓❱

            """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "sync", "sierra", "--all" )).ShouldBeFalse();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Opened branches of 'Test':
            Branch       Repositories                                                         
            stable       4 repositories                                                       
                 ↖ 1 fast-forward, 1 conflict (✱ X-Conflict)                                  
                 ↘ 1 conflict (✱ X-Conflict)                                                  
              => sierra  X-Merge, X-Conflict, 2 unchanged, weight: 2 repositories, 2 projects.

            Links:
              |✋ Manual: nothing is propagated from the parent.
              |> Regular: a version built on the parent is merged.
              -> CI (the default): any commit built on the parent is merged.
              => Full: every commit of the parent's "dev/" branch is merged.

            Merges:
              ↖ "ckli branch close": what closing the branch would merge into its parent.
              ↘ "ckli branch sync": what synchronizing the branch would merge into it.
            ❰✓❱

            """ );
    }

    /// <summary>
    /// A "CI" link only propagates built commits: an unbuilt commit of the parent shows nothing, a CI build of the
    /// parent makes it a fast-forward. The "--link" option predicts the synchronization with another link: with "Full",
    /// the unbuilt commit is a fast-forward.
    /// </summary>
    [Test]
    public async Task branch_list_sync_status_of_a_CI_link_follows_the_parent_builds_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "build", "--regular" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo", "--link", "CI" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "↘" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list", "--link", "Full" )).ShouldBeTrue();
        display.ToString().ShouldContain( "↘ 1 fast-forward" );
        display.ToString().ShouldContain( """↘ "ckli branch sync --link Full": """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list", "-l", "Manual" )).ShouldBeFalse();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "switch", "stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "build" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldContain( "↘ 1 fast-forward" );
    }
}
