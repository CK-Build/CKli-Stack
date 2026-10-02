using CK.Core;
using CKli;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli branch sync" integrates into a branch what its link propagates from its closest existing parent (see
/// <see cref="CKli.BranchModel.Plugin.HotBranch.GetLinkCommit"/>): a "Full" link the parent's "dev/" tip, a "CI"
/// link the parent's last built commit (CI builds included) and a "Release" link its last released one. The merge
/// always targets the "dev/" branch and the optional mode overrides the configured link type.
/// <para>
/// These tests use the fake build harness only. As in <see cref="BranchStartCommitTests"/>, each link type has its
/// own branch name (a name carries its link type in the World's BranchNamespace).
/// </para>
/// </summary>
public class BranchSyncTests
{
    /// <summary>
    /// A "Full" link follows the parent's "dev/" branch: a new commit there is fast-forwarded into the child when
    /// the child has nothing of its own, and merged with a merge commit when it has. Synchronizing an up to date
    /// branch changes nothing.
    /// </summary>
    [Test]
    public async Task a_Full_link_integrates_the_parent_dev_branch_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();

        // Nothing of its own: the parent's new commit is fast-forwarded.
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable" );
        var parentTip = BranchTip( r, "dev/stable" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeTrue();
        WorkTip( r, "sierra" ).ShouldBe( parentTip, "Fast-forwarded to the parent's \"dev/\" tip." );
        // The useless "dev/sierra" that "branch open" left checked out has been auto fixed (deleted, which checks
        // out "sierra") and recreated by the merge: the work goes on in "dev/sierra", never on "sierra".
        CurrentBranch( r ).ShouldBe( "dev/sierra" );

        // A change of its own (on the checked out branch) and a new commit on the parent: a merge commit joins them.
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: null, fileName: "Sierra.txt" );
        var ownTip = WorkTip( r, "sierra" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable" );
        parentTip = BranchTip( r, "dev/stable" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeTrue();
        var merged = WorkTip( r, "sierra" );
        ParentsOf( r, merged ).ShouldBe( [ownTip, parentTip], ignoreOrder: true );

        // Up to date: nothing moves.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeTrue();
        WorkTip( r, "sierra" ).ShouldBe( merged );
    }

    /// <summary>
    /// A "CI" link only receives built commits: an unbuilt commit of the parent is not integrated until a CI build
    /// of the parent covers it.
    /// </summary>
    [Test]
    public async Task a_CI_link_integrates_the_last_built_commit_of_the_parent_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var unbuilt = await BuildOnStableThenLeaveAnUnbuiltCommitAsync( r ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo", "--link", "CI" )).ShouldBeTrue();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "romeo" )).ShouldBeTrue();
        Contains( r, "romeo", unbuilt ).ShouldBeFalse( "The unbuilt commit of 'stable' is not propagated." );

        await CIBuildStableAsync( r ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "romeo" )).ShouldBeTrue();
        Contains( r, "romeo", unbuilt ).ShouldBeTrue( "The CI build of 'stable' covers it: it is propagated." );
    }

    /// <summary>
    /// A "Release" link ignores the CI builds of its parent. The mode overrides the configured link type: a "Full"
    /// synchronization of the same branch integrates the parent's tip, built or not.
    /// </summary>
    [Test]
    public async Task a_Release_link_ignores_CI_builds_unless_the_mode_overrides_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var unbuilt = await BuildOnStableThenLeaveAnUnbuiltCommitAsync( r ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "quebec", "--link", "Release" )).ShouldBeTrue();

        await CIBuildStableAsync( r ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "quebec" )).ShouldBeTrue();
        Contains( r, "quebec", unbuilt ).ShouldBeFalse( "A CI build is not a release." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "quebec", "--mode", "Full" )).ShouldBeTrue();
        Contains( r, "quebec", unbuilt ).ShouldBeTrue( "Full integrates the parent's tip." );
    }

    /// <summary>
    /// A conflicting merge is computed without touching anything: the command fails, says that this must be fixed
    /// manually and the branch stays where it was.
    /// </summary>
    [Test]
    public async Task a_conflict_fails_and_leaves_the_branch_unchanged_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        var before = MakeConflict( r );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Failed merging branch 'dev/stable' into 'dev/sierra'" )
                                     && l.Contains( "This must be fixed manually." ) );
        }
        WorkTip( r, "sierra" ).ShouldBe( before );
    }

    /// <summary>
    /// With --all, a repository that fails does not stop the others: they are synchronized and the command fails.
    /// </summary>
    [Test]
    public async Task with_all_a_conflict_does_not_prevent_the_other_repositories_from_being_synchronized_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rConflict = await world.CreateRepoAsync( "X-Conflict", "v1.0.1" ).ConfigureAwait( false );
        var rClean = await world.CreateRepoAsync( "X-Clean", "v1.0.1" ).ConfigureAwait( false );
        foreach( var r in new[] { rConflict, rClean } )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        }
        var conflictBefore = MakeConflict( rConflict );
        TestHelper.TouchAndCommit( rClean.WorkingFolderPath, branchName: "dev/stable" );
        var cleanParentTip = BranchTip( rClean, "dev/stable" );

        // From one repository: --all is what brings the other one in.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConflict.Root, "branch", "sync", "sierra", "--all" )).ShouldBeFalse();

        WorkTip( rConflict, "sierra" ).ShouldBe( conflictBefore );
        WorkTip( rClean, "sierra" ).ShouldBe( cleanParentTip );
    }

    /// <summary>
    /// A repository with issues is skipped, and a skipped repository is a failure: the branch has not been
    /// synchronized there. Here "sierra" and "dev/sierra" both have a commit of their own (they are desynchronized).
    /// </summary>
    [Test]
    public async Task a_repository_with_issues_fails_the_command_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/sierra" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "sierra", fileName: "OnBase.txt" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable" );
        var before = WorkTip( r, "sierra" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository 'X-Core' has issues: branch 'sierra' has not been synchronized." ) );
        }
        WorkTip( r, "sierra" ).ShouldBe( before );
    }

    /// <summary>
    /// A "Manual" link propagates nothing, so there is nothing to synchronize it with.
    /// </summary>
    [Test]
    public async Task the_mode_cannot_be_Manual_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra", "--mode", "Manual" )).ShouldBeFalse();
            logs.ShouldContain( "Invalid link type 'Manual'. Must be Release, CI or Full." );
        }
    }

    /// <summary>
    /// Commits the same new file with different contents on "dev/sierra" and on "dev/stable".
    /// </summary>
    /// <returns>The sha of the "dev/sierra" tip.</returns>
    static string MakeConflict( FakeBuildRepo repo )
    {
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "dev/sierra", fileContent: _ => "sierra", fileName: "Conflict.txt" );
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "dev/stable", fileContent: _ => "stable", fileName: "Conflict.txt" );
        return WorkTip( repo, "sierra" );
    }

    /// <summary>
    /// Builds the repository so that "stable" carries a release, then leaves an unbuilt commit on "stable".
    /// </summary>
    /// <returns>The sha of the unbuilt commit.</returns>
    static async Task<string> BuildOnStableThenLeaveAnUnbuiltCommitAsync( FakeBuildRepo repo )
    {
        // A change on "dev/stable" (the checked out branch of a fresh repository) and a build: the build
        // integrates "dev/stable" into "stable" and tags it.
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "build", "--release" ).ConfigureAwait( false )).ShouldBeTrue();
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "stable" );
        return BranchTip( repo, "stable" );
    }

    /// <summary>
    /// Switches back to "stable" and builds it in CI.
    /// </summary>
    static async Task CIBuildStableAsync( FakeBuildRepo repo )
    {
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "branch", "switch", "stable" ).ConfigureAwait( false )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "build" ).ConfigureAwait( false )).ShouldBeTrue();
    }

    static string BranchTip( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        var b = e.GitRepository.Repository.Branches[branchName];
        b.ShouldNotBeNull( $"Branch '{branchName}' not found in '{repo.DisplayPath}'." );
        return b.Tip.Sha;
    }

    /// <summary>
    /// Gets the tip of the branch the work of <paramref name="branchName"/> is on: its "dev/" branch when it exists
    /// (a synchronization merges there), the branch itself otherwise.
    /// </summary>
    static string WorkTip( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        var git = e.GitRepository.Repository;
        var b = git.Branches["dev/" + branchName] ?? git.Branches[branchName];
        b.ShouldNotBeNull( $"Branch '{branchName}' not found in '{repo.DisplayPath}'." );
        return b.Tip.Sha;
    }

    static string CurrentBranch( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Head.FriendlyName;
    }

    static string[] ParentsOf( FakeBuildRepo repo, string sha )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Lookup<Commit>( sha ).Parents.Select( p => p.Sha ).ToArray();
    }

    static bool Contains( FakeBuildRepo repo, string branchName, string sha )
    {
        var tip = WorkTip( repo, branchName );
        using var e = repo.CreateEditor();
        var git = e.GitRepository.Repository;
        return git.ObjectDatabase.FindMergeBase( git.Lookup<Commit>( tip ), git.Lookup<Commit>( sha ) )?.Sha == sha;
    }
}
