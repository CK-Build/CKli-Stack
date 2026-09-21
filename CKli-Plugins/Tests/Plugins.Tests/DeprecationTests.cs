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
    /// The "alive" version - the last published stable one, the one every consumer uses today - cannot be
    /// deprecated: that would leave the repository with nothing usable at all. A replacement must be
    /// published first and, once it is, the previous version is no longer alive and can be deprecated.
    /// </summary>
    [Test]
    public async Task the_alive_version_cannot_be_deprecated_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // v1.0.1 is the only published version of X-Core: it is the alive one and is protected.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.1", "--days", "30" )).ShouldBeFalse();

        // Publishing makes v1.0.2 the alive one (and moves the consumer to v0.3.4)...
        await TouchDevStableAsync( rCore ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // ...so v1.0.1 can now be deprecated, and v1.0.2 takes its place as the protected one.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.1", "--days", "30" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.2", "--days", "30" )).ShouldBeFalse();
    }

    /// <summary>
    /// Deprecating a superseded version propagates the "+deprecated" tag to the downstream releases that
    /// consumed it. Since the alive version of every repository is protected, the propagation only ever
    /// reaches superseded releases: nothing has to be rebuilt and no issue is raised.
    /// </summary>
    [Test]
    public async Task deprecation_propagates_to_the_downstream_consumers_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-PerfectEvent", "v0.3.3", references: [rCore] ).ConfigureAwait( false );
        var rDown = await world.CreateRepoAsync( "X-Sample-Monitoring", "v0.0.0", "Samples", rPivot ).ConfigureAwait( false );

        // New code in the pivot: v0.3.4 supersedes v0.3.3 and the downstream follows with v0.0.1.
        await TouchDevStableAsync( rPivot ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // Deprecate the superseded X.PerfectEvent v0.3.3 package, 30 days from now.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "version", "deprecate", "v0.3.3", "--days", "30", "--reason", "For fun." )).ShouldBeTrue();

        // The tag is on the pivot and on the downstream release that consumed it.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rPivot.Root, "tag", "list", "--local" )).ShouldBeTrue();
        display.ToString().ShouldContain( "v0.3.3+deprecated" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rDown.Root, "tag", "list", "--local" )).ShouldBeTrue();
        display.ToString().ShouldContain( "v0.0.0+deprecated" );

        // The alive versions are untouched: there is nothing to build.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  X-Core                      v1.0.1
            -  X-PerfectEvent              v0.3.4
            -  Samples/X-Sample-Monitoring v0.0.1
            There is nothing to build across the 3 repositories and nothing to publish.
            ❰✓❱

            """ );

        // A deprecation is not an issue: nothing to fix anywhere.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );
    }

    /// <summary>
    /// The propagation obeys the same rule as its root: a downstream repository whose alive version still
    /// consumes the deprecated one is not deprecated - that would take away the only version it offers.
    /// The propagation warns and stops there (and on that repository's own consumers).
    /// </summary>
    [Test]
    public async Task the_propagation_stops_on_a_downstream_alive_version_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        // X-Core alone moves to v1.0.2: v1.0.1 becomes superseded, hence deprecatable.
        await TouchDevStableAsync( rCore ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // The consumer is created afterwards: CreateRepoAsync references its upstreams at their
        // InitialVersion, so this repository's alive v0.3.3 consumes the superseded X.Core v1.0.1.
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.1", "--days", "30" )).ShouldBeTrue();

        // X-Core is deprecated, but the propagation left the consumer's alive version alone.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "tag", "list", "--local" )).ShouldBeTrue();
        display.ToString().ShouldContain( "v1.0.1+deprecated" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConsumer.Root, "tag", "list", "--local" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "+deprecated" );
    }

    // The harness commits on "dev/stable": a successful non-CI publication integrates it into "stable"
    // and deletes it, so it may have to be created again.
    static async Task TouchDevStableAsync( FakeBuildRepo repo,
                                           string fileName = "CKliTouchAndCommit.txt",
                                           string? commitMessage = null )
    {
        // "git branch dev/stable" fails when it already exists: the error is ignored on purpose.
        await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "exec", "git", "branch", "dev/stable" );
        TestHelper.TouchAndCommit( repo.WorkingFolderPath,
                                   branchName: "dev/stable",
                                   commitMessage: commitMessage,
                                   fileName: fileName );
    }
}
