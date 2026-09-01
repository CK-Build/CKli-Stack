using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Deprecating a published version. Uses the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class DeprecationTests
{
    /// <summary>
    /// A "+deprecated" version must be rebuilt, and this propagates to the downstream repositories that
    /// consume it. A deprecation to come (--days) is enough: it doesn't wait for the expiration date.
    /// </summary>
    [Test]
    public async Task deprecated_version_must_be_rebuilt_and_propagates_downstream_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-PerfectEvent", "v0.3.3", references: [rCore] ).ConfigureAwait( false );
        var rDown = await world.CreateRepoAsync( "X-Sample-Monitoring", "v0.0.0", "Samples", rPivot ).ConfigureAwait( false );

        // Deprecate the current X.PerfectEvent v0.3.3 package, 30 days from now.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "version", "deprecate", "v0.3.3", "--days", "30", "--reason", "For fun." )).ShouldBeTrue();

        // The pivot must be rebuilt (DeprecatedVersion) and so must its downstream, whose own v0.0.0 becomes
        // deprecated in turn since it embeds the deprecated dependency.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core                      v1.0.1           
            1 -  ⊙   X-PerfectEvent              v0.3.3+deprecated → ⏚/v0.3.4 (DeprecatedVersion)               
            2 -  ·→  Samples/X-Sample-Monitoring v0.0.0+deprecated → ⏚/v0.0.1 (UpstreamBuild, DeprecatedVersion)
            Required build for 2 from the single pivot out of 3 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // A deprecation is not an issue: nothing to fix anywhere.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // Deprecate it now: the plan is the same, the delay never mattered.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "version", "deprecate", "v0.3.3", "--immediate", "--allow-update" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core                      v1.0.1           
            1 -  ⊙   X-PerfectEvent              v0.3.3+deprecated → ⏚/v0.3.4 (DeprecatedVersion)               
            2 -  ·→  Samples/X-Sample-Monitoring v0.0.0+deprecated → ⏚/v0.0.1 (UpstreamBuild, DeprecatedVersion)
            Required build for 2 from the single pivot out of 3 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }
}
