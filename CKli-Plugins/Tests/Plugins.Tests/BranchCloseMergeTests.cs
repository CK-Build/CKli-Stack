using CK.Core;
using CKli;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli branch close" integrates a branch in the "dev/" branch of its closest parent with the same merge as
/// "ckli branch sync": the package versions that conflict are resolved the way a build of the PARENT would update
/// them, and a merge that conflicts beyond them is left in progress in the working folder.
/// </summary>
public class BranchCloseMergeTests
{
    /// <summary>
    /// "juliet" and "stable" have both rewritten the X-Core reference of X-App: closing "juliet" (X-Core is closed with
    /// X-App, its "juliet" version being consumed) gives "dev/stable" the X-Core version that a build of "stable" maps
    /// to, not the "juliet" one. The base "stable" doesn't move.
    /// </summary>
    [Test]
    public async Task closing_resolves_the_package_versions_like_a_build_of_the_parent_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var (core, app) = await BranchSyncTests.ArrangeVersionConflictAsync( world ).ConfigureAwait( false );
        var stableVersion = BranchSyncTests.Reference( app, "dev/stable", core.DefaultProjectName );
        var stableTip = BranchSyncTests.BranchTip( app, "stable" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, app.Root, "branch", "close", "juliet" )).ShouldBeTrue();

        BranchSyncTests.Reference( app, "dev/stable", core.DefaultProjectName ).ShouldBe( stableVersion );
        BranchSyncTests.BranchTip( app, "stable" ).ShouldBe( stableTip );
        using var e = app.CreateEditor();
        e.GitRepository.Repository.Branches["juliet"].ShouldBeNull();
        e.GitRepository.Repository.Branches["dev/juliet"].ShouldBeNull();
    }

    /// <summary>
    /// A dry run of a close that conflicts fails, displays the merge that would be left in progress and changes nothing:
    /// the branch is still there and nothing is merged in "dev/stable".
    /// </summary>
    [Test]
    public async Task a_dry_run_of_a_conflicting_close_fails_and_changes_nothing_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( r.WorkingFolderPath, "dev/romeo", fileContent: _ => "romeo", fileName: "Conflict.txt" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, "dev/stable", fileContent: _ => "stable", fileName: "Conflict.txt" );
        var devStableTip = BranchSyncTests.BranchTip( r, "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "close", "romeo", "--dry-run" )).ShouldBeFalse();
        display.ToString().ShouldContain( "Dry run: 1 conflict. Nothing has been changed." );
        display.ToString().ShouldContain( "⎇ dev/stable ← branch 'romeo'  1 conflict" );
        BranchSyncTests.BranchTip( r, "dev/stable" ).ShouldBe( devStableTip );
        using var e = r.CreateEditor();
        e.GitRepository.Repository.Branches["romeo"].ShouldNotBeNull();
        e.GitRepository.GetSimpleStatusInfo().Operation.ShouldBe( CurrentOperation.None );
    }

    /// <summary>
    /// A real conflict is left in progress on "dev/stable" and the branch stays opened: once the merge is committed,
    /// closing the branch again finds it merged and completes the close.
    /// </summary>
    [Test]
    public async Task a_conflict_is_left_in_progress_and_closing_again_completes_the_close_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( r.WorkingFolderPath, "dev/romeo", fileContent: _ => "romeo", fileName: "Conflict.txt" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, "dev/stable", fileContent: _ => "stable", fileName: "Conflict.txt" );
        var devStableTip = BranchSyncTests.BranchTip( r, "dev/stable" );

        var display = stack.Screen;
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "close", "romeo" )).ShouldBeFalse();
        display.ToString().ShouldBe( """
            A merge is left in progress: resolve its conflicts, commit it (or abort it) and close 'romeo' again.
            > X-Core  ⎇ dev/stable ← branch 'romeo'  1 conflict
            │ Conflict.txt
            ❌ Failed

            """ );
        string romeoTip;
        using( var e = r.CreateEditor() )
        {
            var git = e.GitRepository;
            git.GetSimpleStatusInfo().CurrentBranchName.ShouldBe( "dev/stable" );
            git.GetSimpleStatusInfo().Operation.ShouldBe( CurrentOperation.Merge );
            git.Repository.Branches["dev/stable"].Tip.Sha.ShouldBe( devStableTip, "Nothing is committed." );
            romeoTip = git.Repository.Branches["romeo"].ShouldNotBeNull( "The branch is still opened." ).Tip.Sha;
        }

        File.WriteAllText( r.WorkingFolderPath.AppendPart( "Conflict.txt" ), "resolved" );
        using( var e = r.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            Commands.Stage( git, "Conflict.txt" );
            var signature = new Signature( "CKli.Testing", "none", DateTimeOffset.Now );
            git.Commit( "Merged branch 'romeo'.", signature, signature );
        }
        BranchSyncTests.ParentsOf( r, BranchSyncTests.BranchTip( r, "dev/stable" ) ).ShouldBe( [devStableTip, romeoTip] );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "close", "romeo" )).ShouldBeTrue();
        using( var e = r.CreateEditor() )
        {
            e.GitRepository.Repository.Branches["romeo"].ShouldBeNull();
        }
    }
}
