using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Behavior of the "+fake" version tags. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>): no real
/// "dotnet build" runs, so each test arranges only the repositories it actually needs.
/// </summary>
public class FakeVersionTests
{
    /// <summary>
    /// A "+fake" declares a version to be considered released: the next build produces exactly that
    /// version, however far it is from the current one.
    /// </summary>
    [Test]
    public async Task fake_version_sets_the_version_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        // Jumping to a far future version. Because X-Core's tip already bears v1.0.1, "version bump"
        // carries the "v5.4.3+fake" on a new empty commit (a commit bears at most one version).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "bump", "v5.4.3" )).ShouldBeTrue();

        // The +fake sets the version: the build produces v5.4.3, not a successor of v1.0.1.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();

        // "local/v5.4.3" and not a successor of v1.0.1: the +fake decided the version.
        // v1.0.1 is untouched on its own commit and the +fake is kept until v5.4.3 is published.
        ReadTags( rCore ).ShouldBe( "ckli-repo, local/v5.4.3, v1.0.1, v5.4.3+fake" );

        static string ReadTags( FakeBuildRepo repo )
        {
            using var e = repo.CreateEditor();
            return e.GitRepository.Repository.Tags
                        .Select( t => t.FriendlyName )
                        .Order( StringComparer.Ordinal )
                        .Concatenate();
        }
    }
    [Test]
    public async Task version_bump_and_ci_0_on_fake_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var context = world.WorldRoot;
        var display = stack.Screen;


        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var rActivityMonitor = await world.CreateRepoAsync( "X-ActivityMonitor", "v0.1.0" ).ConfigureAwait( false );
        var rPerfectEvent = await world.CreateRepoAsync( "X-PerfectEvent", "v0.3.2" ).ConfigureAwait( false );
        var rMonitoring = await world.CreateRepoAsync( "X-Monitoring", "v0.2.3" ).ConfigureAwait( false );

        rActivityMonitor.AddOrUpdateReference( rCore, "v1.0.0" );
        rPerfectEvent.AddOrUpdateReference( rActivityMonitor, "v0.1.0" );
        rMonitoring.AddOrUpdateReference( rActivityMonitor, "v0.1.0" );

        // Published version is v1.0.0.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "bump", "v0.1.0" )).ShouldBeFalse( "No way!" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "bump", "v1.0.0" )).ShouldBeFalse( "No way!" );

        // Ok!
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "bump", "v4.3.2" )).ShouldBeTrue();

        // Because we start from a +fake, --ci.0 is the same as --ci.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            v4.3.2+fake → ⏚/v4.3.2--ci.0 (FakeVersion)              
            2 -  X-ActivityMonitor v0.1.0      → ⏚/v0.1.1--ci.2 (UpstreamBuild, CodeChange)
            3 ╓  X-PerfectEvent    v0.3.2      → ⏚/v0.3.3--ci.2 (UpstreamBuild, CodeChange)
            4 ╙  X-Monitoring      v0.2.3      → ⏚/v0.2.4--ci.2 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            v4.3.2+fake → ⏚/v4.3.2--ci.0 (FakeVersion)              
            2 -  X-ActivityMonitor v0.1.0      → ⏚/v0.1.1--ci.2 (UpstreamBuild, CodeChange)
            3 ╓  X-PerfectEvent    v0.3.2      → ⏚/v0.3.3--ci.2 (UpstreamBuild, CodeChange)
            4 ╙  X-Monitoring      v0.2.3      → ⏚/v0.2.4--ci.2 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Building this in CI: the CKt-Core TagCommit is the ci.0 that has an associated FakeVersion.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            v4.3.2+fake → ⏚/v4.3.2--ci.0 (FakeVersion)              
            2 -  X-ActivityMonitor v0.1.0      → ⏚/v0.1.1--ci.2 (UpstreamBuild, CodeChange)
            3 ╓  X-PerfectEvent    v0.3.2      → ⏚/v0.3.3--ci.2 (UpstreamBuild, CodeChange)
            4 ╙  X-Monitoring      v0.2.3      → ⏚/v0.2.4--ci.2 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Because we have NOT published the v4.3.2, we can bump to v3.0.0.
        // => This destroys the "local/v4.3.2--ci.0" release and deletes the "v4.3.2+fake" tag.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "bump", "v3.0.0" )).ShouldBeTrue();


        // Building stable.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            v3.0.0+fake → ⏚/v3.0.0 (FakeVersion)              
            2 -  X-ActivityMonitor v0.1.0      → ⏚/v0.1.1 (UpstreamBuild, CodeChange)
            3 ╓  X-PerfectEvent    v0.3.2      → ⏚/v0.3.3 (UpstreamBuild, CodeChange)
            4 ╙  X-Monitoring      v0.2.3      → ⏚/v0.2.4 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Building --ci.0.
        // The CKt-Core TagCommit is the "local/v3.0.0" with the associated FakeVersion "v3.0.0+fake".
        // The "local/v3.0.0" is deleted by the new "local/v3.0.0--ci.0".
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            (v3.0.0) → ⏚/v3.0.0--ci.0 (CI0)          
            2 -  X-ActivityMonitor (v0.1.1) → ⏚/v0.1.1--ci.4 (UpstreamBuild)
            3 ╓  X-PerfectEvent    (v0.3.3) → ⏚/v0.3.3--ci.4 (UpstreamBuild)
            4 ╙  X-Monitoring      (v0.2.4) → ⏚/v0.2.4--ci.4 (UpstreamBuild)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Because we have NOT published the v3.0.0, we can bump to v2.0.0.
        // => This destroys the "v3.0.0--ci.0" and deletes the "v3.0.0+fake".
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "bump", "v2.0.0" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            v2.0.0+fake    → ⏚/v2.0.0--ci.0 (FakeVersion)  
            2 -  X-ActivityMonitor (v0.1.1--ci.4) → ⏚/v0.1.1--ci.5 (UpstreamBuild)
            3 ╓  X-PerfectEvent    (v0.3.3--ci.4) → ⏚/v0.3.3--ci.5 (UpstreamBuild)
            4 ╙  X-Monitoring      (v0.2.4--ci.4) → ⏚/v0.2.4--ci.5 (UpstreamBuild)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core            v2.0.0+fake → ⏚/v2.0.0 (FakeVersion)              
            2 -  X-ActivityMonitor v0.1.0      → ⏚/v0.1.1 (UpstreamBuild, CodeChange)
            3 ╓  X-PerfectEvent    v0.3.2      → ⏚/v0.3.3 (UpstreamBuild, CodeChange)
            4 ╙  X-Monitoring      v0.2.3      → ⏚/v0.2.4 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories and 4 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }




}
