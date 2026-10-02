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
            stable        1 repository           
              => romeo    No change, 1 unchanged.
                |✋ delta  No change, 1 unchanged.

            Links:
              |✋ Manual: nothing is propagated from the parent.
              |> Release: a version built on the parent is merged.
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
            stable        4 repositories                                                                 
              => bravo    No change, 4 unchanged.                                                        
                => alpha  X-Core and 1 other repository, 2 unchanged, weight: 3 repositories, 3 projects.

            Links:
              |✋ Manual: nothing is propagated from the parent.
              |> Release: a version built on the parent is merged.
              -> CI (the default): any commit built on the parent is merged.
              => Full: every commit of the parent's "dev/" branch is merged.
            ❰✓❱

            """ );
    }
}
