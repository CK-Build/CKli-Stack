using CK.Core;
using CKli;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Where a created branch starts: its <see cref="CKli.BranchModel.Plugin.BranchLinkType"/> decides it, because
/// creation is the only moment the link type can be honored. A later synchronization can only push a branch
/// forward: it can never bring it back to the commit the link says it should have started from.
/// <para>
/// These tests use the fake build harness only. Each link type needs its own branch name (a name carries its
/// link type in the World's BranchNamespace) and its own World, so that the lexicographic parenting of the
/// prerelease branches never gets in the way.
/// </para>
/// </summary>
public class BranchStartCommitTests
{
    /// <summary>
    /// A "CI" link only receives built commits of its parent: the branch starts at the last built commit, not at
    /// the parent's tip.
    /// </summary>
    [Test]
    public async Task a_CI_link_starts_at_the_last_built_commit_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var built = await BuildOnStableThenLeaveAnUnbuiltCommitAsync( r ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo", "--link", "CI" )).ShouldBeTrue();

        BranchTip( r, "romeo" ).ShouldBe( built, "The unbuilt commit of 'stable' is not in 'romeo'." );
    }

    /// <summary>
    /// A "Release" link behaves the same here: the last release of the parent is the commit to start from.
    /// (What separates it from "CI" is that a CI build of the parent doesn't move it - that needs a CI build
    /// to observe, which the harness covers elsewhere.)
    /// </summary>
    [Test]
    public async Task a_Release_link_starts_at_the_last_released_commit_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var built = await BuildOnStableThenLeaveAnUnbuiltCommitAsync( r ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "quebec", "--link", "Release" )).ShouldBeTrue();

        BranchTip( r, "quebec" ).ShouldBe( built, "The unbuilt commit of 'stable' is not in 'quebec'." );
    }

    /// <summary>
    /// "Full" follows the parent's "dev/" branch and "Manual" propagates nothing at all: it starts at the
    /// parent's base branch. A freshly created repository is exactly that fixture: its work sits on "dev/stable"
    /// while "stable" is still at the repository creation commit.
    /// </summary>
    [Test]
    public async Task a_Full_link_starts_at_the_parent_dev_branch_and_a_Manual_one_at_its_base_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rFull = await world.CreateRepoAsync( "X-Full", "v1.0.1" ).ConfigureAwait( false );
        var rManual = await world.CreateRepoAsync( "X-Manual", "v1.0.1" ).ConfigureAwait( false );
        foreach( var r in new[] { rFull, rManual } )
        {
            BranchTip( r, "dev/stable" ).ShouldNotBe( BranchTip( r, "stable" ), "The \"dev/\" branch is ahead." );
        }

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rFull.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rManual.Root, "branch", "open", "tango", "--link", "Manual" )).ShouldBeTrue();

        BranchTip( rFull, "sierra" ).ShouldBe( BranchTip( rFull, "dev/stable" ) );
        BranchTip( rManual, "tango" ).ShouldBe( BranchTip( rManual, "stable" ) );
    }

    /// <summary>
    /// The other half of the rule: when the last built commit cannot be found, a "Release" or "CI" branch is not
    /// created at some fallback commit - the command fails. A freshly created repository has nothing built on
    /// "stable" (its version tag is on "dev/stable"), so it cannot open a branch that starts from a release.
    /// </summary>
    [Test]
    public async Task a_Release_link_fails_when_the_parent_has_nothing_built_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "quebec", "--link", "Release" )).ShouldBeFalse();

        using var e = r.CreateEditor();
        e.GitRepository.Repository.Branches["quebec"].ShouldBeNull( "The branch has not been created." );
    }

    /// <summary>
    /// Builds the repository so that "stable" carries a release, then leaves an unbuilt commit on "stable".
    /// </summary>
    /// <returns>The sha of the built commit: the last built commit of "stable", which is not its tip anymore.</returns>
    static async Task<string> BuildOnStableThenLeaveAnUnbuiltCommitAsync( FakeBuildRepo repo )
    {
        // A change on "dev/stable" (the checked out branch of a fresh repository) and a build: the build
        // integrates "dev/stable" into "stable" and tags it.
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "build", "--release" ).ConfigureAwait( false )).ShouldBeTrue();
        var built = BranchTip( repo, "stable" );
        VersionTagsOn( repo, built ).ShouldNotBeEmpty( "The build tagged the tip of 'stable'." );

        // An unbuilt commit on "stable": its tip is no longer its last built commit.
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "stable" );
        BranchTip( repo, "stable" ).ShouldNotBe( built );
        return built;
    }

    static string BranchTip( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        var b = e.GitRepository.Repository.Branches[branchName];
        b.ShouldNotBeNull( $"Branch '{branchName}' not found in '{repo.DisplayPath}'." );
        return b.Tip.Sha;
    }

    // A build tags "v1.0.2" once published and "local/v1.0.2" until then: both are built versions.
    static bool IsVersionTag( string friendlyName ) => friendlyName.StartsWith( 'v' ) || friendlyName.Contains( "/v" );

    static List<string> VersionTagsOn( FakeBuildRepo repo, string sha )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                .Where( t => IsVersionTag( t.FriendlyName ) && t.PeeledTarget is Commit c && c.Sha == sha )
                .Select( t => t.FriendlyName )
                .ToList();
    }
}
