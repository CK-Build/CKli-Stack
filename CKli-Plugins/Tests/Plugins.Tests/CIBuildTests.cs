using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Behavior of the CI builds ("--ci" and "--ci.0"). These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class CIBuildTests
{
    /// <summary>
    /// "--ci.0" produces a 0 depth CI version only for the repositories that have no upstream (rank 0): a
    /// repository that depends on a rebuilt one is de facto an UpstreamBuild, hence at least "ci.1".
    /// </summary>
    [Test]
    public async Task ci_0_only_applies_to_repositories_without_upstream_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        // From the stack root, every repository is a pivot: only X-Core (no upstream) gets the ci.0.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core     v1.0.1 → ⏚/v1.0.2--ci.0 (CI0)          
            2 -  X-Consumer v0.1.1 → ⏚/v0.1.2--ci.1 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // From X-Consumer, it is the single pivot: it gets the ci.0 and X-Core is left alone.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConsumer.Root, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   X-Core     v1.0.1
            1 -  ⊙   X-Consumer v0.1.1 → ⏚/v0.1.2--ci.0 (CI0)
            Required build for 1 from the single pivot out of 2 repositories and 1 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // "*build" pulls the upstream closure in, so X-Core is rebuilt and X-Consumer becomes an
        // UpstreamBuild: its ci.0 is lost, exactly as from the stack root.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConsumer.Root, "*build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 - →·   X-Core     v1.0.1 → ⏚/v1.0.2--ci.0 (CI0)          
            2 -  ⊙   X-Consumer v0.1.1 → ⏚/v0.1.2--ci.1 (UpstreamBuild)
            Required build for 2 from the single pivot out of 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }

    /// <summary>
    /// Publishing a CI version and then a non-CI one from the same code produces the non-CI version of the
    /// same base: the CI publication doesn't consume the version.
    /// </summary>
    [TestCase( true )]
    [TestCase( false )]
    public async Task ci_publish_followed_by_non_ci_publish_Async( bool useCheckout )
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.3.3" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.0.0", references: [rCore] ).ConfigureAwait( false );

        await TouchDevStableAsync( rCore, useCheckout ).ConfigureAwait( false );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core     v0.3.3 → ⏚/v0.3.4--ci.1 (CodeChange)   
            2 -  X-Consumer v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Same code, non-CI: the CI publication above did not consume v0.3.4.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core     v0.3.3 → ⏚/v0.3.4 (CodeChange)               
            2 -  X-Consumer v0.0.0 → ⏚/v0.0.1 (UpstreamBuild, CodeChange)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }

    /// <summary>
    /// Commits a change on the repository's "dev/stable" branch, creating it either through ckli
    /// (<paramref name="useCheckout"/>) or directly through git.
    /// </summary>
    static async Task TouchDevStableAsync( FakeBuildRepo repo, bool useCheckout )
    {
        if( useCheckout )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        }
        else
        {
            // "git branch dev/stable" fails if it already exists: the error is ignored on purpose.
            await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "exec", "git", "branch", "dev/stable" );
        }
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "dev/stable" );
    }

}
