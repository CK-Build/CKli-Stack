using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Behavior of the "local/" (built but unpublished) releases. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class LocalReleaseTests
{
    /// <summary>
    /// Rebuilding an unpublished ("local/") release MOVES it onto the new commit: building new code a second
    /// time must produce the same version again, not the next one.
    /// </summary>
    [Test]
    public async Task rebuilding_local_moves_the_version_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.0.0", references: [rCore] ).ConfigureAwait( false );

        // Both repositories are up to date with regard to their version.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  X-Core     v0.3.3
            -  X-Consumer v0.0.0
            There is nothing to build across the 2 repositories and nothing to publish.
            ❰✓❱

            """ );

        // New code in X-Core: it builds v0.3.4 and the consumer follows with v0.0.1.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  ⊙   X-Core     v0.3.3 → ⏚/v0.3.4 (CodeChange)   
            2 -  ·→  X-Consumer v0.0.0 → ⏚/v0.0.1 (UpstreamBuild)
            Required build for 2 from the single pivot out of 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // More new code. THIS is the subject: v0.3.4 and v0.0.1 are built but NOT published, so they must be
        // MOVED onto the new commits. v0.3.5 and v0.0.2 must not appear. A local current version is
        // displayed parenthesized.
        // (The successful non-CI build integrated "dev/stable" into "stable" and deleted it: recreate it.)
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  ⊙   X-Core     (v0.3.4) → ⏚/v0.3.4 (CodeChange)   
            2 -  ·→  X-Consumer (v0.0.1) → ⏚/v0.0.1 (UpstreamBuild)
            Required build for 2 from the single pivot out of 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }
}
