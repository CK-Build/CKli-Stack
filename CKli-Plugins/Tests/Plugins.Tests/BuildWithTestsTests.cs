using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The tests column of the roadmap and "build --with-tests" that runs the tests of the repositories that are not
/// built. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class BuildWithTestsTests
{
    /// <summary>
    /// "build --skip-tests" leaves untested versions that a following "build" doesn't build again: the tests column
    /// shows them ('?') with the '--with-tests' hint. "build --with-tests" runs their tests, after which they are
    /// tested ('✓'). "--force-tests" with "--with-tests" runs them again.
    /// </summary>
    [Test]
    public async Task build_with_tests_runs_the_tests_of_the_repositories_that_are_not_built_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.0.0", references: [rCore] ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--with-tests", "--skip-tests" )).ShouldBeFalse();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--skip-tests" )).ShouldBeTrue();
        rCore.TestRunCount.ShouldBe( 0 );
        rConsumer.TestRunCount.ShouldBe( 0 );

        // Nothing to build: the tests that have been skipped don't run.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  ⊙  ?  X-Core     ⏚/v0.3.4--ci.1
            -  ·→ ?  X-Consumer ⏚/v0.0.1--ci.1
            There is nothing to build from the single pivot out of 2 repositories but 2 can be published.
            (Using '*build' may detect required builds in upstreams repositories.)
            (2 repositories that are not built have never been tested: use '--with-tests' to test them.)
            ❰✓❱

            """ );
        rCore.TestRunCount.ShouldBe( 0 );
        rConsumer.TestRunCount.ShouldBe( 0 );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--with-tests" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  ⊙  T  X-Core     ⏚/v0.3.4--ci.1
            -  ·→ T  X-Consumer ⏚/v0.0.1--ci.1
            There is nothing to build from the single pivot out of 2 repositories but 2 can be published.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );
        rCore.TestRunCount.ShouldBe( 1 );
        rConsumer.TestRunCount.ShouldBe( 1 );

        // Now tested: "--with-tests" has nothing to do.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--with-tests" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  ⊙  ✓  X-Core     ⏚/v0.3.4--ci.1
            -  ·→ ✓  X-Consumer ⏚/v0.0.1--ci.1
            There is nothing to build from the single pivot out of 2 repositories but 2 can be published.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );
        rCore.TestRunCount.ShouldBe( 1 );
        rConsumer.TestRunCount.ShouldBe( 1 );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--with-tests", "--force-tests" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  ⊙  T  X-Core     ⏚/v0.3.4--ci.1
            -  ·→ T  X-Consumer ⏚/v0.0.1--ci.1
            There is nothing to build from the single pivot out of 2 repositories but 2 can be published.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );
        rCore.TestRunCount.ShouldBe( 2 );
        rConsumer.TestRunCount.ShouldBe( 2 );
    }

    /// <summary>
    /// A build that builds a repository and tests another one: the built one runs its tests as usual, the other
    /// one with "--with-tests". A failing test fails the command.
    /// </summary>
    [Test]
    public async Task build_with_tests_fails_on_failing_tests_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.0.0", references: [rCore] ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--skip-tests" )).ShouldBeTrue();

        // A change in X-Consumer only: X-Core is not built, X-Consumer is (and is tested).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConsumer.Root, "branch", "switch", "dev/stable" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rConsumer.WorkingFolderPath, "dev/stable" );

        rCore.FailTests = true;
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-tests" )).ShouldBeFalse();
        display.ToString().ShouldBe( """
              - T  X-Core     ⏚/v0.3.4--ci.1
            1 - T  X-Consumer (v0.0.1--ci.1) → ⏚/v0.0.1--ci.2 (CodeChange)
            Required build for 1 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
             i  X-Consumer ⏚/v0.0.1--ci.2 built: 1 package.
            ❌ Failed

            """ );
        rCore.TestRunCount.ShouldBe( 1 );
        rConsumer.TestRunCount.ShouldBe( 1 );
    }
}
