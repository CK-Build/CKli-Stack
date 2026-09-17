using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli branch list" is the only consumer of <see cref="CKli.BranchModel.Plugin.BranchNamespace.GetDisplayTree"/>:
/// the compact link codes exist for this display and for nothing else - the configuration and the "--link"
/// option of "ckli branch open" both spell a link type by its name.
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
        display.ToString().ShouldContain( """
            stable
              => romeo
                |✋ delta
            """ );
        display.ToString().ShouldContain( "Links:", "The codes come with their legend." );
    }
}
