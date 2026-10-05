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
