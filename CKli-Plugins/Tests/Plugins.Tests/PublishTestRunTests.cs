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
/// possibly with "--skip-tests") run their tests first (BuildPlugin.EnsureTestedAsync) and there is no
/// "--skip-tests" on a publication. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class PublishTestRunTests
{
    /// <summary>
    /// "build --skip-tests" then "publish": the publication has nothing to build, it runs the tests of the
    /// releases it publishes: the tests column of the roadmap says so. A second test run of the same content is
    /// never done.
    /// </summary>
    [Test]
    public async Task publish_runs_the_tests_of_releases_built_without_them_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.0.0", references: [rCore] ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "build", "--skip-tests" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  ⊙  ✗  X-Core     v0.3.3 → ⏚/v0.3.4--ci.1 (CodeChange)   
            2 -  ·→ ✗  X-Consumer v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 2 from the single pivot out of 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
             i  X-Core ⏚/v0.3.4--ci.1 built: 1 package.
             i  X-Consumer ⏚/v0.0.1--ci.1 built: 1 package.
            ❰✓❱

            """ );
        rCore.TestRunCount.ShouldBe( 0 );
        rConsumer.TestRunCount.ShouldBe( 0 );

        // The dry run announces the test runs in the tests column.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  ⊙  T  X-Core     ⏚/v0.3.4--ci.1
            -  ·→ T  X-Consumer ⏚/v0.0.1--ci.1
            There is nothing to build from the single pivot out of 2 repositories but 2 can be published.
            (Using '*publish' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );
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
    /// Failing tests stop the publication before anything is published: there is no "--skip-tests" to publish
    /// anyway, the tests must be fixed.
    /// </summary>
    [Test]
    public async Task failing_tests_block_the_publication_Async()
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
            logs.ShouldContain( "Unable to publish: the tests of a release failed. Nothing is published untested: fix them." );
        }
        rCore.TestRunCount.ShouldBe( 1 );

        // "--skip-tests" is not an option of the publication.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish", "--skip-tests" )).ShouldBeFalse();
        rCore.TestRunCount.ShouldBe( 1 );

        rCore.FailTests = false;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "publish" )).ShouldBeTrue();
        rCore.TestRunCount.ShouldBe( 2 );
    }
}
