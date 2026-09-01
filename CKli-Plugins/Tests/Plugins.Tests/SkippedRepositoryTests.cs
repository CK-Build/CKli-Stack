using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Behavior of the repositories that a pivot-scoped build skips. These tests use the fake build harness
/// only (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class SkippedRepositoryTests
{
    /// <summary>
    /// A repository that is out of the pivots scope is skipped even when its sources reference packages produced by
    /// this World in versions that have been superseded (these 'U' updates are "skippable", see the canSkip in
    /// Roadmap.BuildSolution.Initialize).
    /// <para>
    /// This must NOT abort the build: since the repository is not built, nothing it produces enters the build and the
    /// publication is unaffected. The pending updates are warned about, rendered on its (not built) row and left
    /// pending.
    /// </para>
    /// <para>
    /// The misalignment then survives the publication: only a build of that repository resolves it. This is what the
    /// second part of this test exhibits, from the stack root where nothing is skippable: the very same pending 'U'
    /// update becomes a MustBuildReason.DependencyUpdate.
    /// </para>
    /// </summary>
    [Test]
    public async Task skipped_repository_keeps_its_pending_updates_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        // X-Monitoring is a SIBLING of the X-PerfectEvent pivot: both reference X-ActivityMonitor but neither
        // depends on the other, so a build from X-PerfectEvent skips X-Monitoring.
        var rUp = await world.CreateRepoAsync( "X-ActivityMonitor", "v0.1.0" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-PerfectEvent", "v0.3.3", references: [rUp] ).ConfigureAwait( false );
        var rSibling = await world.CreateRepoAsync( "X-Monitoring", "v0.2.4", references: [rUp] ).ConfigureAwait( false );
        var rDown = await world.CreateRepoAsync( "X-Sample", "v0.0.0", references: [rPivot] ).ConfigureAwait( false );

        // The upstream must actually produce a NEW version, otherwise v0.1.0 stays what this World offers and
        // there is nothing for the sibling to be misaligned with. Publishing from the root realigns everybody.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rUp.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--ci", "--branch", "stable" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-ActivityMonitor v0.1.0 → ⏚/v0.1.1--ci.1 (CodeChange)   
            2 ╓  X-PerfectEvent    v0.3.3 → ⏚/v0.3.4--ci.1 (UpstreamBuild)
            3 ╙  X-Monitoring      v0.2.4 → ⏚/v0.2.5--ci.1 (UpstreamBuild)
            4 -  X-Sample          v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Now misalign the skipped sibling: its sources reference a version of X.ActivityMonitor that this
        // World no longer offers, while its own last CI build tag stays aligned (so this is NOT detected as a
        // MustBuildReason.UpstreamVersion).
        rSibling.AddOrUpdateReference( rUp, "v0.1.0", branchName: "dev/stable" );

        // Touch the pivot so this publication has something to build.
        TestHelper.TouchAndCommit( rPivot.WorkingFolderPath, branchName: null );

        // The publication succeeds: the skipped sibling only warns about its pending updates.
        display.Clear();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "publish", "--ci", "--branch", "stable" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "is skipped but its sources require dependency updates." ) );
        }
        display.ToString().ShouldBe( """
              - →·   X-ActivityMonitor v0.1.1--ci.1
            1 ╓  ⊙   X-PerfectEvent    v0.3.4--ci.1 → ⏚/v0.3.4--ci.2                        (CodeChange)   
              ╙      X-Monitoring      v0.2.5--ci.1 U X.ActivityMonitor: 0.1.0 → 0.1.1--ci.1
            2 -  ·→  X-Sample          v0.0.1--ci.1 → ⏚/v0.0.1--ci.2                        (UpstreamBuild)
            Required build for 2 from the single pivot out of 4 repositories and 2 can be published.
            U 1 update from upstreams.
            ❰✓❱

            """ );

        // From the stack root every solution is a pivot (<==> none of them is): nothing is skippable, so the
        // very same pending update becomes a DependencyUpdate and the sibling is built.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--ci", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  X-ActivityMonitor v0.1.1--ci.1
              ╓  X-PerfectEvent    v0.3.4--ci.2
            1 ╙  X-Monitoring      v0.2.5--ci.1 → ⏚/v0.2.5--ci.3 (DependencyUpdate, CodeChange)          
                                                                 U X.ActivityMonitor: 0.1.0 → 0.1.1--ci.1
              -  X-Sample          v0.0.1--ci.2
            Required build for 1 repositories across the 4 repositories and 1 can be published.
            U 1 update from upstreams.
            ❰✓❱

            """ );

        // A "*build" from the pivot produces the same plan: it prevents skipping, not pivots.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "*build", "--ci", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-ActivityMonitor v0.1.1--ci.1
              ╓  ⊙   X-PerfectEvent    v0.3.4--ci.2
            1 ╙      X-Monitoring      v0.2.5--ci.1 → ⏚/v0.2.5--ci.3 (DependencyUpdate, CodeChange)          
                                                                     U X.ActivityMonitor: 0.1.0 → 0.1.1--ci.1
              -  ·→  X-Sample          v0.0.1--ci.2
            Required build for 1 from the single pivot out of 4 repositories and 1 can be published.
            U 1 update from upstreams.
            ❰✓❱

            """ );
    }
}
