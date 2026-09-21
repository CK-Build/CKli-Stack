using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Switching to CI after a release build. A "ckli build --release" leaves a pending "local/" release on the
/// commit; asking for a CI build afterwards must not be a dead end.
/// <para>
/// The two halves are deliberately asymmetric. A pending "local/" release is unpublished, so a plain CI build
/// reclaims it (MustBuildReason.RollingLocal - TagCommit.CanBearVersion's "rolling local build" case already
/// sanctions it). A PUBLISHED version cannot be reclaimed, so a CI build on its commit opens a new version
/// line and stays behind "--ci.0" - and the empty roadmap says so instead of leaving the user guessing.
/// </para>
/// </summary>
public class RollingLocalCIBuildTests
{
    /// <summary>
    /// "ckli build --release" then "ckli build": the pending local releases are rolled into CI versions. Before
    /// MustBuildReason.RollingLocal this answered "There is nothing to build" and only "--ci.0" could do it.
    /// </summary>
    [Test]
    public async Task a_CI_build_rolls_a_pending_local_release_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );

        // The regular build: this is the "by mistake" one. It leaves "local/" releases behind.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();
        VersionTags( rCore ).ShouldContain( "local/v1.0.2" );

        display.Clear();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
            logs.ShouldContain( """
                'X-Core (stable)' has a pending local release 'local/v1.0.2' that no publication consumed.
                Building it in CI takes its place on the same commit: the pending release is destroyed.
                """ );
        }
        display.ToString().ShouldBe( """
            1 -  X-Core     (v1.0.2) → ⏚/v1.0.2--ci.1 (RollingLocal) 
            2 -  X-Consumer (v0.1.2) → ⏚/v0.1.2--ci.2 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // The pending release has been reclaimed: one version per commit.
        VersionTags( rCore ).ShouldBe( ["local/v1.0.2--ci.1", "v1.0.1"], ignoreOrder: true );

        // And it is now genuinely up to date: nothing left to build, and no "--ci.0" hint since the last
        // build is a CI version.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  X-Core     ⏚/v1.0.2--ci.1
            -  X-Consumer ⏚/v0.1.2--ci.2
            There is nothing to build across the 2 repositories but 2 can be published.
            ❰✓❱

            """ );
    }

    /// <summary>
    /// A published version is another matter: it cannot be reclaimed, so a plain CI build still has nothing to do and
    /// "--ci.0" opens a new version line above it. What changes is that the empty roadmap now names "--ci.0".
    /// </summary>
    [Test]
    public async Task an_empty_CI_roadmap_names_the_ci_0_option_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.1", references: [rCore] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();
        VersionTags( rCore ).ShouldContain( "v1.0.2" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  X-Core     v1.0.2
            -  X-Consumer v0.1.2
            There is nothing to build across the 2 repositories and nothing to publish.
            (Use '--ci.0' to build a CI version from the 2 repositories that already carry a released version.)
            ❰✓❱

            """ );

        // Following the hint works, and the published versions are left alone.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core     v1.0.2 → ⏚/v1.0.3--ci.0 (CI0)          
            2 -  X-Consumer v0.1.2 → ⏚/v0.1.3--ci.1 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
        VersionTags( rCore ).ShouldBe( ["local/v1.0.3--ci.0", "v1.0.1", "v1.0.2"], ignoreOrder: true );
    }

    /// <summary>
    /// The hint is about the plain CI build only: a "--release" build is not asking for a CI version, and
    /// "--ci.0" is the option the user already used.
    /// </summary>
    [Test]
    public async Task the_ci_0_hint_is_not_displayed_outside_a_plain_ci_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "--ci.0" );

        // "--ci.0" itself builds, so it never reaches the empty roadmap here: run it twice to get there.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--ci.0" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "Use '--ci.0'" );
    }

    static string[] VersionTags( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                .Select( t => t.FriendlyName )
                .Where( n => n.StartsWith( 'v' ) || n.Contains( "/v" ) )
                .ToArray();
    }
}
