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
/// A publication never publishes an untested release: the releases it carries but did not build (built before,
/// possibly with "--skip-tests") run their tests first (BuildPlugin.EnsureTestedAsync). These tests use the fake
/// build harness only (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class PublishTestRunTests
{
    /// <summary>
    /// "build --skip-tests" then "publish": the publication has nothing to build, it runs the tests of the
    /// releases it publishes. A second test run of the same content is never done.
    /// </summary>
    [Test]
    public async Task publish_runs_the_tests_of_releases_built_without_them_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.0.0", references: [rCore] ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--skip-tests" )).ShouldBeTrue();
        rCore.TestRunCount.ShouldBe( 0 );
        rConsumer.TestRunCount.ShouldBe( 0 );

        // The dry run announces the test runs.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish", "--dry-run" )).ShouldBeTrue();
            logs.ShouldContain( t => t.StartsWith( "2 release(s) to publish have never been tested: the publication will run their tests." ) );
        }
        rCore.TestRunCount.ShouldBe( 0 );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish" )).ShouldBeTrue();
            logs.ShouldContain( "Running the tests of 2 release(s) to publish that have never been tested." );
            logs.Where( t => t.StartsWith( "Tests of '" ) && t.EndsWith( "' passed." ) ).Count().ShouldBe( 2 );
        }
        rCore.TestRunCount.ShouldBe( 1 );
        rConsumer.TestRunCount.ShouldBe( 1 );
    }

    /// <summary>
    /// A release whose tests ran when it was built is not tested again by the publication.
    /// </summary>
    [Test]
    public async Task publish_does_not_test_again_a_tested_release_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build" )).ShouldBeTrue();
        rCore.TestRunCount.ShouldBe( 1 );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish" )).ShouldBeTrue();
        rCore.TestRunCount.ShouldBe( 1 );
    }

    /// <summary>
    /// Failing tests stop the publication before anything is published; "--skip-tests" publishes anyway, with a
    /// warning that names the untested releases.
    /// </summary>
    [Test]
    public async Task failing_tests_block_the_publication_unless_skip_tests_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--skip-tests" )).ShouldBeTrue();

        rCore.FailTests = true;
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish" )).ShouldBeFalse();
            logs.ShouldContain( t => t.StartsWith( "Tests of 'X-Core/v" ) && t.EndsWith( "' failed: it cannot be published." ) );
            logs.ShouldContain( "Unable to publish: the tests of a release failed. Fix them, or use '--skip-tests' to publish anyway." );
            logs.ShouldNotContain( t => t.StartsWith( "Publishing " ) );
        }
        rCore.TestRunCount.ShouldBe( 1 );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish", "--skip-tests" )).ShouldBeTrue();
            logs.ShouldContain( t => t.StartsWith( "Publishing 1 release(s) that have never been tested (--skip-tests):" ) );
        }
        rCore.TestRunCount.ShouldBe( 1 );
    }
}
