using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A missing root branch is the ultimate branch issue: it blocks every branch command that reads the repository
/// until "ckli issue --fix" solves it. "ckli branch list" and "ckli branch close" read every repository of the World,
/// the other ones the repositories in scope.
/// </summary>
public class MissingRootBranchTests
{
    [Test]
    public async Task a_missing_root_branch_blocks_the_branch_commands_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rNoRoot = await world.CreateRepoAsync( "X-NoRoot", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        using( var e = rNoRoot.CreateEditor() )
        {
            e.GitRepository.Repository.Branches.Remove( "stable" );
        }

        await ShouldBeBlockedAsync( world.WorldRoot, "branch", "list" ).ConfigureAwait( false );
        // X-NoRoot is not in the scope but the close reads every repository of the World.
        await ShouldBeBlockedAsync( r.Root, "branch", "close", "sierra" ).ConfigureAwait( false );
        await ShouldBeBlockedAsync( rNoRoot.Root, "branch", "sync", "sierra" ).ConfigureAwait( false );
        await ShouldBeBlockedAsync( rNoRoot.Root, "branch", "switch", "sierra" ).ConfigureAwait( false );
        await ShouldBeBlockedAsync( rNoRoot.Root, "branch", "switch", "sierra", "--create" ).ConfigureAwait( false );
        await ShouldBeBlockedAsync( rNoRoot.Root, "branch", "open", "romeo" ).ConfigureAwait( false );

        using( var e = r.CreateEditor() )
        {
            e.GitRepository.Repository.Branches["sierra"].ShouldNotBeNull( "The close has not been done." );
        }
    }

    static async Task ShouldBeBlockedAsync( CKliEnv context, params string[] command )
    {
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, command ).ConfigureAwait( false )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Missing root 'stable' branch in 'X-NoRoot'. Use 'ckli issue --fix' to fix this." ) );
        }
    }
}
