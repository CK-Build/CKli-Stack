using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli maintenance migrate net8" works on the repositories selected by the current directory, like
/// "ckli pull", "ckli status" or "ckli deps update" do. "--all" widens the scope to the whole World and
/// "--hard-reset-all" implies it (deleting every working folder leaves the current directory unable to
/// select anything: it may itself be one of the folders being deleted).
/// <para>
/// The observable used here is the "dev/stable" branch that the migration always ensures
/// (BranchLink.EnsureDevBranch): a non-CI "ckli build" integrates "dev/stable" into "stable" and DELETES
/// it, so an arranged and built World has none - and the repositories the migration did not select keep
/// it that way.
/// </para>
/// </summary>
public class MigrationScopeTests
{
    /// <summary>
    /// From inside a repository, only that repository is migrated. From the World root (or with "--all"
    /// from anywhere), all of them are.
    /// </summary>
    [Test]
    public async Task migrate_net8_works_on_the_selected_repositories_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        // Two independent repositories: a build of one cannot drag the other along.
        var rA = await world.CreateRepoAsync( "X-Alpha", "v0.1.0" ).ConfigureAwait( false );
        var rB = await world.CreateRepoAsync( "X-Beta", "v0.2.0" ).ConfigureAwait( false );

        // The arrange is quiescent: without something to build, the build has nothing to integrate.
        TestHelper.TouchAndCommit( rA.WorkingFolderPath, branchName: null );
        TestHelper.TouchAndCommit( rB.WorkingFolderPath, branchName: null );

        // A non-CI build integrates "dev/stable" into "stable" and deletes it. This is also what puts the
        // solution file on "stable": the migration's ".slnx" normalization needs it there.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        BranchExists( rA, "dev/stable" ).ShouldBeFalse( "The build integrated and deleted it." );
        BranchExists( rB, "dev/stable" ).ShouldBeFalse( "The build integrated and deleted it." );

        // From inside X-Alpha: X-Beta is not selected and stays untouched.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rA.Root, "maintenance", "migrate", "net8" )).ShouldBeTrue();
        BranchExists( rA, "dev/stable" ).ShouldBeTrue( "X-Alpha is the selected repository." );
        BranchExists( rB, "dev/stable" ).ShouldBeFalse( "X-Beta has not been selected." );

        // Still from inside X-Alpha, "--all" widens the scope to the whole World.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rA.Root, "maintenance", "migrate", "net8", "--all" )).ShouldBeTrue();
        BranchExists( rB, "dev/stable" ).ShouldBeTrue( "--all ignores the current directory." );
    }

    /// <summary>
    /// The stack folder is neither in nor above a Repo, so it selects nothing: this is an error, not a
    /// silent no-op that would let a migration look like it ran.
    /// </summary>
    [Test]
    public async Task migrate_net8_refuses_a_path_that_selects_no_repository_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        await world.CreateRepoAsync( "X-Alpha", "v0.1.0" ).ConfigureAwait( false );

        var stackFolder = world.WorldRoot.ChangeDirectory(
                                stack.StackRoot.AppendPart( stack.IsPublic
                                                                ? StackRepository.PublicStackName
                                                                : StackRepository.PrivateStackName ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, stackFolder, "maintenance", "migrate", "net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Unable to find any Repo from path" ) );
        }
    }

    static bool BranchExists( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Branches[branchName] != null;
    }
}
