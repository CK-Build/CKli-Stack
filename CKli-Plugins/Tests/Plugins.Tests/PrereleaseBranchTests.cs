using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Behavior of a prerelease branch (here "romeo") and how its versions propagate through the graph.
/// These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class PrereleaseBranchTests
{
    /// <summary>
    /// Opening a "romeo" branch linked to Full on a repository: the prerelease versions it produces
    /// propagate to its downstream repositories, and "*build" brings its upstreams into "romeo" too.
    /// </summary>
    [Test]
    public async Task romeo_with_Full_on_Stable_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        // A chain: X-Core <- X-ActivityMonitor <- X-PerfectEvent (the pivot) <- X-Sample.
        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rMonitor = await world.CreateRepoAsync( "X-ActivityMonitor", "v0.1.1", references: [rCore] ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-PerfectEvent", "v0.3.3", references: [rMonitor] ).ConfigureAwait( false );
        var rSample = await world.CreateRepoAsync( "X-Sample", "v0.0.0", references: [rPivot] ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "status" )).ShouldBeTrue();
        display.ToString().ShouldContain( "X-PerfectEvent ⎇ dev/romeo (untracked)" );

        // Opening the branch changed no code: nothing to build.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   X-Core            v1.0.1
            - →·   X-ActivityMonitor v0.1.1
            -  ⊙   X-PerfectEvent    v0.3.3
            -  ·→  X-Sample          v0.0.0
            There is nothing to build from the single pivot out of 4 repositories and nothing to publish.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );

        // But "--ci.0" forces a romeo prerelease, which makes the romeo branch appear downstream.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1
              - →·   X-ActivityMonitor v0.1.1
            1 -  ⊙   X-PerfectEvent    v0.3.3 → ⏚/v0.3.4-romeo.0.ci.0 (CI0)          
            2 -  ·→  X-Sample          v0.0.0 → ⏚/v0.0.1-romeo.0.ci.1 (UpstreamBuild)
            Required build for 2 from the single pivot out of 4 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Rather than the ci.0, touch the pivot to obtain the 2 first "real" romeo prereleases.
        TestHelper.TouchAndCommit( rPivot.WorkingFolderPath, branchName: null );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1
              - →·   X-ActivityMonitor v0.1.1
            1 -  ⊙   X-PerfectEvent    v0.3.3 → ⏚/v0.3.4-romeo (CodeChange)   
            2 -  ·→  X-Sample          v0.0.0 → ⏚/v0.0.1-romeo (UpstreamBuild)
            Required build for 2 from the single pivot out of 4 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // A "feat:" commit followed by 2 fixes: the feat belongs to the TagCommitTree's head commits and
        // must be found, so the change is a Minor one.
        TestHelper.TouchAndCommit( rPivot.WorkingFolderPath, branchName: null, commitMessage: "feat: Some feature." );
        TestHelper.TouchAndCommit( rPivot.WorkingFolderPath, branchName: null, commitMessage: "fix 1" );
        TestHelper.TouchAndCommit( rPivot.WorkingFolderPath, branchName: null, commitMessage: "fix 2" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1
              - →·   X-ActivityMonitor v0.1.1
            1 -  ⊙   X-PerfectEvent    v0.3.3 → ⏚/v0.4.0-romeo (CodeChange)   
            2 -  ·→  X-Sample          v0.0.0 → ⏚/v0.1.0-romeo (UpstreamBuild)
            Required build for 2 from the single pivot out of 4 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Touch an UPSTREAM of the pivot, on its "dev/stable".
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rMonitor.Root, "branch", "switch", "dev/stable" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rMonitor.Root, "status" )).ShouldBeTrue();
        display.ToString().ShouldContain( "X-ActivityMonitor ⎇ dev/stable (untracked)" );
        TestHelper.TouchAndCommit( rMonitor.WorkingFolderPath, branchName: null, commitMessage: "fix in core." );

        // From the pivot, that upstream change is not visible: nothing to build.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   X-Core            v1.0.1        
            - →·   X-ActivityMonitor v0.1.1        
            -  ⊙   X-PerfectEvent    ⏚/v0.4.0-romeo
            -  ·→  X-Sample          ⏚/v0.1.0-romeo
            There is nothing to build from the single pivot out of 4 repositories but 2 can be published.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );

        // Not even in CI.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--ci", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   X-Core            v1.0.1        
            - →·   X-ActivityMonitor v0.1.1        
            -  ⊙   X-PerfectEvent    ⏚/v0.4.0-romeo
            -  ·→  X-Sample          ⏚/v0.1.0-romeo
            There is nothing to build from the single pivot out of 4 repositories but 2 can be published.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );

        // "--ci.0" forces CI versions. The ci versions replace the non-ci ones: there is only one
        // "local/" at a time per branch name.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1        
              - →·   X-ActivityMonitor v0.1.1        
            1 -  ⊙   X-PerfectEvent    (v0.4.0-romeo) → ⏚/v0.4.0-romeo.0.ci.4 (CI0)          
            2 -  ·→  X-Sample          (v0.1.0-romeo) → ⏚/v0.1.0-romeo.0.ci.2 (UpstreamBuild)
            Required build for 2 from the single pivot out of 4 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // "*build" pulls the upstream closure in: the touched upstream and its downstream go to romeo.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "*build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1        
            1 - →·   X-ActivityMonitor v0.1.1         → ⏚/v0.1.2-romeo (CodeChange)   
            2 -  ⊙   X-PerfectEvent    (v0.4.0-romeo) → ⏚/v0.4.0-romeo (UpstreamBuild)
            3 -  ·→  X-Sample          (v0.1.0-romeo) → ⏚/v0.1.0-romeo (UpstreamBuild)
            Required build for 3 from the single pivot out of 4 repositories and 3 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // And "*build --ci" creates the romeo ci versions.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "*build", "--ci", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1        
            1 - →·   X-ActivityMonitor v0.1.1         → ⏚/v0.1.2-romeo.0.ci.1 (CodeChange)   
            2 -  ⊙   X-PerfectEvent    (v0.4.0-romeo) → ⏚/v0.4.0-romeo.0.ci.5 (UpstreamBuild)
            3 -  ·→  X-Sample          (v0.1.0-romeo) → ⏚/v0.1.0-romeo.0.ci.2 (UpstreamBuild)
            Required build for 3 from the single pivot out of 4 repositories and 3 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Really run it.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "*build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1        
            1 - →·   X-ActivityMonitor v0.1.1         → ⏚/v0.1.2-romeo.0.ci.1 (CodeChange)   
            2 -  ⊙   X-PerfectEvent    (v0.4.0-romeo) → ⏚/v0.4.0-romeo.0.ci.5 (UpstreamBuild)
            3 -  ·→  X-Sample          (v0.1.0-romeo) → ⏚/v0.1.0-romeo.0.ci.2 (UpstreamBuild)
            Required build for 3 from the single pivot out of 4 repositories and 3 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // And now in non-CI.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "*build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core            v1.0.1
            1 - →·   X-ActivityMonitor v0.1.1 → ⏚/v0.1.2-romeo (CodeChange)               
            2 -  ⊙   X-PerfectEvent    v0.3.3 → ⏚/v0.4.0-romeo (UpstreamBuild, CodeChange)
            3 -  ·→  X-Sample          v0.0.0 → ⏚/v0.1.0-romeo (UpstreamBuild, CodeChange)
            Required build for 3 from the single pivot out of 4 repositories and 3 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }
}
