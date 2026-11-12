using CK.Core;
using CKli;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A "dev/" branch implies its base. The BranchModel's AutoFixDevBranch (true by default) keeps the pair consistent:
/// a useless "dev/" branch is deleted, and the missing base of a "dev/" branch is recreated where the "dev/" branch left
/// its parent. When it is false, "ckli issue --fix" does the same. A CI publication of a "dev/" branch pushes its base
/// when the remote doesn't have it, so that no other clone fetches an orphan "dev/" branch.
/// </summary>
public class DevBranchTests
{
    /// <summary>
    /// "ckli issue" doesn't fix anything: it reports the missing base as an implicit issue (Ⓘ), the one that the
    /// other commands fix silently. The next command recreates "sierra" where "dev/sierra" left "dev/stable", which is
    /// where "sierra" was: nothing of "dev/sierra" is lost.
    /// </summary>
    [Test]
    public async Task the_missing_base_of_a_dev_branch_is_recreated_where_it_left_its_parent_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var (baseTip, devTip) = ArrangeOrphanDevBranch( r );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "issue" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Ⓘ Missing base branches." );
        display.ToString().ShouldContain( "'dev/sierra' lacks its base 'sierra' branch." );
        Tip( r, "sierra" ).ShouldBeNull( "\"ckli issue\" fixes nothing." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "list" )).ShouldBeTrue();
        Tip( r, "sierra" ).ShouldBe( baseTip );
        Tip( r, "dev/sierra" ).ShouldBe( devTip );
    }

    /// <summary>
    /// Without the auto fix, the missing base is a real issue (⚙) that "ckli issue --fix" fixes the same way.
    /// </summary>
    [Test]
    public async Task without_AutoFixDevBranch_ckli_issue_fix_recreates_the_missing_base_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "BranchModel.AutoFixDevBranch", "false" )).ShouldBeTrue();
        var (baseTip, devTip) = ArrangeOrphanDevBranch( r );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "issue" )).ShouldBeTrue();
        display.ToString().ShouldContain( "⚙ Missing base branches." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "issue", "--fix" )).ShouldBeTrue();
        Tip( r, "sierra" ).ShouldBe( baseTip );
        Tip( r, "dev/sierra" ).ShouldBe( devTip );
    }

    /// <summary>
    /// The base branch "juliet" tracks "origin/juliet" but the remote doesn't have it: the CI publication of
    /// "dev/juliet" pushes it too.
    /// </summary>
    [Test]
    public async Task a_CI_publication_pushes_the_base_branch_that_the_remote_lacks_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "build", "--regular" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "juliet", "--link", "CI" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: null );
        using( var e = r.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            var juliet = git.Branches["juliet"];
            git.Branches.Update( juliet, u => { u.Remote = "origin"; u.UpstreamBranch = juliet.CanonicalName; } );
        }

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "publish" )).ShouldBeTrue();

        using var bare = new Repository( stack.Remotes.GetUriFor( "X-Core" ).LocalPath );
        bare.Branches["dev/juliet"].ShouldNotBeNull();
        bare.Branches["juliet"].ShouldNotBeNull( "The base of the published \"dev/\" branch is on the remote." );
    }

    /// <summary>
    /// A non CI build integrates "dev/stable" into "stable" and deletes it, but "origin/dev/stable" stays where it was
    /// until the publication pushes: when it fails before (CK World, 2026-11-11), "origin/dev/stable" is behind the local
    /// "stable" that holds the unpushed work. The next command must not recreate "dev/stable" from it: a CI build would
    /// then build (and a CI publication push) the code without that work.
    /// </summary>
    [Test]
    public async Task an_integrated_remote_dev_branch_is_not_recreated_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable" );
        string pushedTip;
        using( var e = r.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            git.Network.Push( git.Network.Remotes["origin"], "refs/heads/dev/stable:refs/heads/dev/stable" );
            var dev = git.Branches["dev/stable"];
            git.Branches.Update( dev, u => { u.Remote = "origin"; u.UpstreamBranch = dev.CanonicalName; } );
            pushedTip = dev.Tip.Sha;
            git.Branches["origin/dev/stable"].ShouldNotBeNull().Tip.Sha.ShouldBe( pushedTip );
        }
        // The work that is not pushed.
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable", commitMessage: "Unpushed work." );
        var unpushedWork = Tip( r, "dev/stable" ).ShouldNotBeNull();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "build", "--regular" )).ShouldBeTrue();
        Tip( r, "dev/stable" ).ShouldBeNull( "The regular build integrated 'dev/stable' into 'stable'." );
        // The integration deleted the remote tracking branch with its local branch: the next fetch (the next publish
        // fetches) brings it back.
        using( var e = r.CreateEditor() )
        {
            Commands.Fetch( e.GitRepository.Repository, "origin", [], null, null );
        }
        Tip( r, "origin/dev/stable" ).ShouldBe( pushedTip, "Nothing has been pushed." );

        // Any command loads the branches: "origin/dev/stable" is in "stable", "dev/stable" is not recreated from it.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "list" )).ShouldBeTrue();
        Tip( r, "dev/stable" ).ShouldBeNull( "'origin/dev/stable' is already integrated in 'stable'." );

        // A CI build works on a "dev/stable" that starts from "stable" and tracks "origin/dev/stable".
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: null, commitMessage: "New work." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "build" )).ShouldBeTrue();
        using( var e = r.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            var dev = git.Branches["dev/stable"].ShouldNotBeNull( "The CI build works on 'dev/stable'." );
            git.ObjectDatabase.FindMergeBase( git.Lookup<Commit>( unpushedWork ), dev.Tip )?.Sha
                .ShouldBe( unpushedWork, "The CI build is built on the unpushed work." );
            dev.TrackedBranch?.CanonicalName.ShouldBe( "refs/remotes/origin/dev/stable" );
        }
    }

    /// <summary>
    /// Opens "sierra" with a Full link (it starts at the "dev/stable" tip), commits on "dev/sierra" and deletes "sierra".
    /// </summary>
    /// <returns>The commits of the deleted "sierra" and of "dev/sierra".</returns>
    static (string BaseTip, string DevTip) ArrangeOrphanDevBranch( FakeBuildRepo r )
    {
        CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" ).GetAwaiter().GetResult().ShouldBeTrue();
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/sierra" );
        var baseTip = Tip( r, "sierra" ).ShouldNotBeNull();
        var devTip = Tip( r, "dev/sierra" ).ShouldNotBeNull();
        using( var e = r.CreateEditor() )
        {
            e.GitRepository.Repository.Branches.Remove( "sierra" );
        }
        return (baseTip, devTip);
    }

    static string? Tip( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Branches[branchName]?.Tip.Sha;
    }
}
