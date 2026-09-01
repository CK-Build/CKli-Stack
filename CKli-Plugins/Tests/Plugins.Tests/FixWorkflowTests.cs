using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The Fix Workflow: fixing a version that is no longer in the "hot zone".
/// <see cref="CKli.HotZone.Plugin.HotZonePlugin.FixStartAsync"/>,
/// <see cref="CKli.HotZone.Plugin.HotZonePlugin.FixInfo"/>,
/// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuildAsync"/>.
/// Uses the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class FixWorkflowTests
{
    [Test]
    public async Task fix_workflow_on_a_chain_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var rMonitor = await world.CreateRepoAsync( "X-ActivityMonitor", "v0.1.0", references: [rCore] ).ConfigureAwait( false );
        var rDown = await world.CreateRepoAsync( "X-Monitoring", "v0.2.0", references: [rMonitor] ).ConfigureAwait( false );

        // No v2 at all.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "start", "v2" )).ShouldBeFalse();
            logs.ShouldContain( "Unable to find any version to fix for 'v2'." );
        }

        // Nothing to cancel is not an error.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "cancel" )).ShouldBeTrue();
            logs.ShouldContain( "No current workflow exist." );
        }

        // v1.0 is the last published stable: it belongs to the hot zone, the regular workflow handles it.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "start", "v1.0" )).ShouldBeFalse();
            logs.ShouldContain( """
                The version to fix 'v1.0.0' is in the "hot zone" (the last published stable version is 'v1.0.0').
                Use the regular workflow with 'ckli build/publish' commands to produce a fix.
                """ );
        }

        // Publish a v1.1.0 of X-Core: the "feat:" conventional commit triggers the Minor increment, and v1.0
        // leaves the hot zone.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null, commitMessage: "feat: some feature." );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            v1.0.0 → ⏚/v1.1.0 (CodeChange)   
            2 -  X-ActivityMonitor v0.1.0 → ⏚/v0.2.0 (UpstreamBuild)
            3 -  X-Monitoring      v0.2.0 → ⏚/v0.3.0 (UpstreamBuild)
            Required build for 3 repositories across the 3 repositories and 3 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Now v1.0 can be fixed: one "fix/vMajor.Minor" branch per repository of the closure.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "start", "v1.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on X-Core:
            1 - X-Core            ⎇ fix/v1.0 → v1.0.1 
            2 - X-ActivityMonitor ⎇ fix/v0.1 → v0.1.1 
            3 - X-Monitoring      ⎇ fix/v0.2 → v0.2.1 
            ❰✓❱

            """ );

        // "fix info" displays the current workflow.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "info" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on X-Core:
            1 - X-Core            ⎇ fix/v1.0 → v1.0.1 
            2 - X-ActivityMonitor ⎇ fix/v0.1 → v0.1.1 
            3 - X-Monitoring      ⎇ fix/v0.2 → v0.2.1 
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              X-Core            ⎇ fix/v1.0  → v1.0.1--ci.2
              X-ActivityMonitor ⎇ fix/v0.1  → v0.1.1--ci.3
              X-Monitoring      ⎇ fix/v0.2  → v0.2.1--ci.3
            ❰✓❱

            """ );

        // Second "fix build --ci" with no change: everything is skipped.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "build", "--ci" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "Useless build for 'X-Core/" ) && l.Contains( "skipped." ) );
            display.ToString().ShouldBe( """
                  X-Core            ⎇ fix/v1.0    v1.0.1--ci.2
                  X-ActivityMonitor ⎇ fix/v0.1    v0.1.1--ci.3
                  X-Monitoring      ⎇ fix/v0.2    v0.2.1--ci.3
                ❰✓❱

                """ );
        }

        // A change in the middle of the chain, on its fix branch.
        TestHelper.TouchAndCommit( rMonitor.WorkingFolderPath, branchName: "fix/v0.1" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              X-Core            ⎇ fix/v1.0  → v1.0.1
              X-ActivityMonitor ⎇ fix/v0.1  → v0.1.1
              X-Monitoring      ⎇ fix/v0.2  → v0.2.1
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              X-Core            ⎇ fix/v1.0    v1.0.1
              X-ActivityMonitor ⎇ fix/v0.1    v0.1.1
              X-Monitoring      ⎇ fix/v0.2    v0.2.1
            ❰✓❱

            """ );

        // A successfully published workflow is deleted: there is nothing left to cancel.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "cancel" )).ShouldBeTrue();
            logs.ShouldContain( "No current workflow exist." );
        }

        // Fixing again starts from the version that was just published.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "start", "v1.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.1' on X-Core:
            1 - X-Core            ⎇ fix/v1.0 → v1.0.2 
            2 - X-ActivityMonitor ⎇ fix/v0.1 → v0.1.2 
            3 - X-Monitoring      ⎇ fix/v0.2 → v0.2.2 
            ❰✓❱

            """ );
    }
}
