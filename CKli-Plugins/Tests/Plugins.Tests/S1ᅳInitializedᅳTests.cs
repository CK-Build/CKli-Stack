using CK.Core;
using CKli;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

public class S1ᅳInitializedᅳTests
{
    /// <summary>
    /// <see cref="CKli.HotZone.Plugin.HotZonePlugin.FixStartAsync"/>
    /// <see cref="CKli.HotZone.Plugin.HotZonePlugin.FixInfo"/>
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuildAsync"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task local_fix_Async()
    {
        Helper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        // cd CK-Core.
        var cktCoreContext = context.ChangeDirectory( "CKt-Core" );

        // No v2 yet => "Unable to find any version to fix for 'v2'.".
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v2" )).ShouldBeFalse();
            logs.ShouldContain( "Unable to find any version to fix for 'v2'." );
        }

        // "ckli fix cancel" with nothing to cancel (no error, just an info that there's nothing to cancel).
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "cancel" )).ShouldBeTrue();
            logs.ShouldContain( "No current workflow exist." );
        }

        // v1.0 is the last stable. No way.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v1.0" )).ShouldBeFalse();
            logs.ShouldContain( """
                The version to fix 'v1.0.0' is in the "hot zone" (the last published stable version is 'v1.0.0').
                Use the regular workflow with 'ckli build/publish' commands to produce a fix.
                """ );
        }

        // Let's publish a v1.1 of CKt-Core. The commit message that starts with "feat:" (conventional commit) triggers
        // a minor's increment.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "branch", "switch", "dev/stable" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( cktCoreContext.CurrentDirectory, "dev/stable", "feat: some feature." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "publish" )).ShouldBeTrue();

        // Now we can do: ckli fix start v1.0
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v1.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on CKt-Core:
            1 - CKt-Core            ⎇ fix/v1.0 → v1.0.1 
            2 - CKt-ActivityMonitor ⎇ fix/v0.1 → v0.1.1 
            3 ╓ CKt-PerfectEvent    ⎇ fix/v0.2 → v0.2.2 
            4 ║ CKt-PerfectEvent    ⎇ fix/v0.3 → v0.3.3 
            5 ╙ CKt-Monitoring      ⎇ fix/v0.2 → v0.2.4 
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "info" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on CKt-Core:
            1 - CKt-Core            ⎇ fix/v1.0 → v1.0.1 
            2 - CKt-ActivityMonitor ⎇ fix/v0.1 → v0.1.1 
            3 ╓ CKt-PerfectEvent    ⎇ fix/v0.2 → v0.2.2 
            4 ║ CKt-PerfectEvent    ⎇ fix/v0.3 → v0.3.3 
            5 ╙ CKt-Monitoring      ⎇ fix/v0.2 → v0.2.4 
            ❰✓❱

            """ );

        // ckli fix build --ci
        using( TestHelper.Monitor.OpenInfo( """
            First 'ckli fix build --ci' => triggers the Net8 migration.
            This handles NuGet.config => nuget.config, removing RepositoryInfo.xml, transforming .sln to .slnx, removing CodeCakeBuilder...
            """ ) )
        {
            // The first (CKt-Core) is ci.1 and the following ci.2 because of the "Net8 migration applied".
            // Without this commit they would be ci.0 and ci.1. 
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build", "--ci" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
                  CKt-Core            ⎇ fix/v1.0  → v1.0.1--ci.1
                  CKt-ActivityMonitor ⎇ fix/v0.1  → v0.1.1--ci.2
                  CKt-PerfectEvent    ⎇ fix/v0.2  → v0.2.2--ci.2
                  CKt-PerfectEvent    ⎇ fix/v0.3  → v0.3.3--ci.2
                  CKt-Monitoring      ⎇ fix/v0.2  → v0.2.4--ci.2
                ❰✓❱

                """ );

        }

        // ckli fix build --ci
        using( TestHelper.Monitor.OpenInfo( "Second 'ckli fix build --ci' (no change), all are skipped." ) )
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build", "--ci" )).ShouldBeTrue();
            logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1--ci.1' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-ActivityMonitor/0.1.1--ci.2' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.2.2--ci.2' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.3.3--ci.2' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-Monitoring/0.2.4--ci.2' skipped." );
            display.ToString().ShouldBe( """
                  CKt-Core            ⎇ fix/v1.0    v1.0.1--ci.1
                  CKt-ActivityMonitor ⎇ fix/v0.1    v0.1.1--ci.2
                  CKt-PerfectEvent    ⎇ fix/v0.2    v0.2.2--ci.2
                  CKt-PerfectEvent    ⎇ fix/v0.3    v0.3.3--ci.2
                  CKt-Monitoring      ⎇ fix/v0.2    v0.2.4--ci.2
                ❰✓❱

                """ );

        }

        var cktActivityMonitor = context.ChangeDirectory( "CKt-ActivityMonitor" );
        TestHelper.TouchAndCommit( cktActivityMonitor.CurrentDirectory, branchName: "fix/v0.1" );

        using( TestHelper.Monitor.OpenInfo( "'ckli fix build' NOT in ci (CKt.ActivityMonitor has changed)." ) )
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
                          CKt-Core            ⎇ fix/v1.0  → v1.0.1
                          CKt-ActivityMonitor ⎇ fix/v0.1  → v0.1.1
                          CKt-PerfectEvent    ⎇ fix/v0.2  → v0.2.2
                          CKt-PerfectEvent    ⎇ fix/v0.3  → v0.3.3
                          CKt-Monitoring      ⎇ fix/v0.2  → v0.2.4
                        ❰✓❱

                        """ );
        }

        using( TestHelper.Monitor.OpenInfo( "'ckli fix publish' (no change in the code base: there's nothing to build) but all must be published." ) )
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "publish" )).ShouldBeTrue();
            logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-ActivityMonitor/0.1.1' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.2.2' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.3.3' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-Monitoring/0.2.4' skipped." );
            display.ToString().ShouldBe( """
                          CKt-Core            ⎇ fix/v1.0    v1.0.1
                          CKt-ActivityMonitor ⎇ fix/v0.1    v0.1.1
                          CKt-PerfectEvent    ⎇ fix/v0.2    v0.2.2
                          CKt-PerfectEvent    ⎇ fix/v0.3    v0.3.3
                          CKt-Monitoring      ⎇ fix/v0.2    v0.2.4
                        ❰✓❱

                        """ );
        }

        // "ckli fix cancel": nothing to cancel, a successfully published workflow is deleted.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "cancel" )).ShouldBeTrue();
            logs.ShouldContain( "No current workflow exist." );
        }

        // ckli fix start v1.0 (again).
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v1.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.1' on CKt-Core:
            1 - CKt-Core            ⎇ fix/v1.0 → v1.0.2 
            2 - CKt-ActivityMonitor ⎇ fix/v0.1 → v0.1.2 
            3 ╓ CKt-PerfectEvent    ⎇ fix/v0.2 → v0.2.3 
            4 ║ CKt-PerfectEvent    ⎇ fix/v0.3 → v0.3.4 
            5 ╙ CKt-Monitoring      ⎇ fix/v0.2 → v0.2.5 
            ❰✓❱

            """ );

        // Starting a new fix on CKt-Core: "Fixing 'v1.0.1'..."
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v1.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.1' on CKt-Core:
            1 - CKt-Core            ⎇ fix/v1.0 → v1.0.2 
            2 - CKt-ActivityMonitor ⎇ fix/v0.1 → v0.1.2 
            3 ╓ CKt-PerfectEvent    ⎇ fix/v0.2 → v0.2.3 
            4 ║ CKt-PerfectEvent    ⎇ fix/v0.3 → v0.3.4 
            5 ╙ CKt-Monitoring      ⎇ fix/v0.2 → v0.2.5 
            ❰✓❱

            """ );

        var cktMonitoring = context.ChangeDirectory( "CKt-Monitoring" );
        TestHelper.TouchAndCommit( cktMonitoring.CurrentDirectory, branchName: "fix/v0.2" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              CKt-Core            ⎇ fix/v1.0  → v1.0.2--ci.0
              CKt-ActivityMonitor ⎇ fix/v0.1  → v0.1.2--ci.1
              CKt-PerfectEvent    ⎇ fix/v0.2  → v0.2.3--ci.1
              CKt-PerfectEvent    ⎇ fix/v0.3  → v0.3.4--ci.1
              CKt-Monitoring      ⎇ fix/v0.2  → v0.2.5--ci.2
            ❰✓❱

            """ );

        // No build required...
        // TODO: Fix the ci.0 special case that triggers a useless recompilation.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              CKt-Core            ⎇ fix/v1.0  → v1.0.2--ci.0
              CKt-ActivityMonitor ⎇ fix/v0.1    v0.1.2--ci.1
              CKt-PerfectEvent    ⎇ fix/v0.2    v0.2.3--ci.1
              CKt-PerfectEvent    ⎇ fix/v0.3    v0.3.4--ci.1
              CKt-Monitoring      ⎇ fix/v0.2    v0.2.5--ci.2
            ❰✓❱

            """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              CKt-Core            ⎇ fix/v1.0  → v1.0.2
              CKt-ActivityMonitor ⎇ fix/v0.1  → v0.1.2
              CKt-PerfectEvent    ⎇ fix/v0.2  → v0.2.3
              CKt-PerfectEvent    ⎇ fix/v0.3  → v0.3.4
              CKt-Monitoring      ⎇ fix/v0.2  → v0.2.5
            ❰✓❱

            """ );

   }

    [Test]
    public async Task version_bump_and_ci_0_on_fake_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        var cktCore = context.ChangeDirectory( "CKt-Core" );

        // Published version is v1.0.0.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCore, "version", "bump", "v0.1.0" )).ShouldBeFalse( "No way!" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCore, "version", "bump", "v1.0.0" )).ShouldBeFalse( "No way!" );

        // Ok!
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCore, "version", "bump", "v4.3.2" )).ShouldBeTrue();

        // Because we start from a +fake, --ci.0 is the same as --ci.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v4.3.2+fake → 🡡/v4.3.2--ci.0 (FakeVersion)              
            2 -  CKt-ActivityMonitor v0.1.0      → 🡡/v0.1.1--ci.4 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent    v0.3.2      → 🡡/v0.3.3--ci.4 (UpstreamBuild, CodeChange)
            4 ╙  CKt-Monitoring      v0.2.3      → 🡡/v0.2.4--ci.4 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v4.3.2+fake → 🡡/v4.3.2--ci.0 (FakeVersion)              
            2 -  CKt-ActivityMonitor v0.1.0      → 🡡/v0.1.1--ci.4 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent    v0.3.2      → 🡡/v0.3.3--ci.4 (UpstreamBuild, CodeChange)
            4 ╙  CKt-Monitoring      v0.2.3      → 🡡/v0.2.4--ci.4 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );

        // Building this in CI: the CKt-Core TagCommit is the ci.0 that has an associated FakeVersion.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v4.3.2+fake → 🡡/v4.3.2--ci.0 (FakeVersion)              
            2 -  CKt-ActivityMonitor v0.1.0      → 🡡/v0.1.1--ci.4 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent    v0.3.2      → 🡡/v0.3.3--ci.4 (UpstreamBuild, CodeChange)
            4 ╙  CKt-Monitoring      v0.2.3      → 🡡/v0.2.4--ci.4 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );

        // Because we have NOT published the v4.3.2, we can bump to v3.0.0.
        // => This destroys the "local/v4.3.2--ci.0" release and deletes the "v4.3.2+fake" tag.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCore, "version", "bump", "v3.0.0" )).ShouldBeTrue();

        // Building stable.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v3.0.0+fake → 🡡/v3.0.0 (FakeVersion)              
            2 -  CKt-ActivityMonitor v0.1.0      → 🡡/v0.1.1 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent    v0.3.2      → 🡡/v0.3.3 (UpstreamBuild, CodeChange)
            4 ╙  CKt-Monitoring      v0.2.3      → 🡡/v0.2.4 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );

        // Building --ci.0.
        // The CKt-Core TagCommit is the "local/v3.0.0" with the associated FakeVersion "v3.0.0+fake".
        // The "local/v3.0.0" is deleted by the new "local/v3.0.0--ci.0".
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v3.0.0- → 🡡/v3.0.0--ci.0 (CI0)          
            2 -  CKt-ActivityMonitor v0.1.1- → 🡡/v0.1.1--ci.6 (UpstreamBuild)
            3 ╓  CKt-PerfectEvent    v0.3.3- → 🡡/v0.3.3--ci.6 (UpstreamBuild)
            4 ╙  CKt-Monitoring      v0.2.4- → 🡡/v0.2.4--ci.6 (UpstreamBuild)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );

        // Because we have NOT published the v3.0.0, we can bump to v2.0.0.
        // => This destroys the "v3.0.0--ci.0" and deletes the "v3.0.0+fake".
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCore, "version", "bump", "v2.0.0" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v2.0.0+fake   → 🡡/v2.0.0--ci.0 (FakeVersion)  
            2 -  CKt-ActivityMonitor v0.1.1--ci.6- → 🡡/v0.1.1--ci.7 (UpstreamBuild)
            3 ╓  CKt-PerfectEvent    v0.3.3--ci.6- → 🡡/v0.3.3--ci.7 (UpstreamBuild)
            4 ╙  CKt-Monitoring      v0.2.4--ci.6- → 🡡/v0.2.4--ci.7 (UpstreamBuild)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v2.0.0+fake → 🡡/v2.0.0 (FakeVersion)              
            2 -  CKt-ActivityMonitor v0.1.0      → 🡡/v0.1.1 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent    v0.3.2      → 🡡/v0.3.3 (UpstreamBuild, CodeChange)
            4 ╙  CKt-Monitoring      v0.2.3      → 🡡/v0.2.4 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );



    }



    [Test]
    public async Task CKt_add_sample_and_ci_Async()
    {
        Helper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        var inSampleFolder = context.ChangeDirectory( "Samples" );

        var newRepo1 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-Sample-Monitoring" );
        var newRepoUrl1 = $"file://{newRepo1}";
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "create", newRepoUrl1 )).ShouldBeTrue();

        var newRepo2 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-App-Sample" );
        var newRepoUrl2 = $"file://{newRepo2}";
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "create", newRepoUrl2 )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > Samples/CKt-Sample-Monitoring (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'main'.
            > Samples/CKt-App-Sample (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'main'.
            ❰✓❱

            """ );
        // This one can be fixed with a dirty folder (no need to commit). 
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "issue", "--fix" )).ShouldBeTrue();
        display.Clear();

        #region Initializing Samples/CKt-Sample-Monitoring
        {
            var inSampleMonitoring = inSampleFolder.ChangeDirectory( "CKt-Sample-Monitoring" );
            Directory.Exists( inSampleMonitoring.CurrentDirectory ).ShouldBeTrue();

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "branch", "switch", "dev/stable" )).ShouldBeTrue();

            var path = inSampleMonitoring.CurrentDirectory.AppendPart( "CKt.Sample.Monitoring" );
            Directory.CreateDirectory( path );
            File.WriteAllText( path.AppendPart( "CKt.Sample.Monitoring.csproj" ), """
                <Project Sdk="Microsoft.NET.Sdk">

                    <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <Nullable>enable</Nullable>
                        <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                    </PropertyGroup>

                    <ItemGroup>
                        <PackageReference Include="CKt.Monitoring" Version="0.2.3" />
                        <PackageReference Include="CKt.PerfectEvent" Version="0.3.2" />
                    </ItemGroup>

                </Project>

                """ );
            File.WriteAllText( path.AppendPart( "PreserveAssemblyReference.cs" ), """
                using System;

                namespace CKt.Sample.Monitoring;

                public record PreserveAssemblyReference( CKt.Monitoring.PreserveAssemblyReference Monitoring,
                                                         CKt.PerfectEvent.PreserveAssemblyReference PerfectEvent );

                """ );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "exec", "dotnet", "new", "sln" )).ShouldBeTrue();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "exec", "dotnet", "sln", "add", "CKt.Sample.Monitoring/CKt.Sample.Monitoring.csproj" )).ShouldBeTrue();

            var deployFolder = inSampleMonitoring.CurrentDirectory.AppendPart( ArtifactHandlerPlugin.DeployFolderName );
            Directory.CreateDirectory( deployFolder );
            File.WriteAllText( deployFolder.AppendPart( "GenerateAssets.cs" ), """
                #:property PublishAot=false
                using System;
                using System.IO;

                File.WriteAllText( $"Assets/Install-{args[0]}.txt", $"I'm the install manual of CKt-Sample-Monitoring version '{args[0]}'." );
                """ );
            File.WriteAllText( deployFolder.AppendPart( ".gitignore" ), "Assets/" );
        }
        #endregion

        #region Initializing Samples/CKt-App-Sample
        {
            var inSampleApp = inSampleFolder.ChangeDirectory( "CKt-App-Sample" );
            Directory.Exists( inSampleApp.CurrentDirectory ).ShouldBeTrue();

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "branch", "switch", "dev/stable" )).ShouldBeTrue();

            var path = inSampleApp.CurrentDirectory.AppendPart( "CKt.SomeApp" );
            Directory.CreateDirectory( path );
            File.WriteAllText( path.AppendPart( "CKt.SomeApp.csproj" ), """
            <Project Sdk="Microsoft.NET.Sdk">

                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                </PropertyGroup>

                <ItemGroup>
                    <PackageReference Include="CKt.ActivityMonitor" Version="0.1.0" />
                </ItemGroup>

            </Project>

            """ );
            File.WriteAllText( path.AppendPart( "PreserveAssemblyReference.cs" ), """
            using System;

            namespace CKt.SomeApp;

            public record PreserveAssemblyReference( CKt.ActivityMonitor.PreserveAssemblyReference ActivityMonitor );

            """ );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "exec", "dotnet", "new", "sln" )).ShouldBeTrue();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "exec", "dotnet", "sln", "add", "CKt.SomeApp/CKt.SomeApp.csproj" )).ShouldBeTrue();

            var deployFolder = inSampleApp.CurrentDirectory.AppendPart( ArtifactHandlerPlugin.DeployFolderName );
            Directory.CreateDirectory( deployFolder );
            File.WriteAllText( deployFolder.AppendPart( "GenerateAssets.cs" ), """
                #:property PublishAot=false
                using System;
                using System.IO;

                Directory.CreateDirectory( "Assets/ZipDemo" );
                File.WriteAllText( $"Assets/ZipDemo/Install-{args[0]}.txt", "I'm the install manual of CKt.SomeApp version '{args[0]}'." );
                File.WriteAllText( $"Assets/ZipDemo/AnotherFile.txt", "Another file..." );
                
                """ );
            File.WriteAllText( deployFolder.AppendPart( ".gitignore" ), "Assets/" );
        }
        #endregion


        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "switch", "dev/stable" )).ShouldBeTrue();

        // The nuget.config can be fixed with a dirty folder (no need to pre-commit here).
        // => Creates the missing nuget.config file and updated the feed urls in existing ones:
        //    this is the work of the ArtifactHandlerPlugin plugin and the BranchModel/HotBranch/ContentIssue.
        //
        // But the "Missing initial version." requires a clean working folder.
        // 
        // ... so we commit.

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > Samples/CKt-Sample-Monitoring (2)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by creating a 'v0.0.0+fake' on 'stable' branch.
            > Samples/CKt-App-Sample (2)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by creating a 'v0.0.0+fake' on 'stable' branch.
            ❰✓❱

            """ );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "commit", "Initialized CKt-Sample-Monitoring and CKt-App-Sample." )).ShouldBeTrue();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();

        // No more issue.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // Let's build (but not publish yet) the CI versions.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.0      → 🡡/v1.0.1--ci.4 (CodeChange)                            
            2 -  CKt-ActivityMonitor           v0.1.0      → 🡡/v0.1.1--ci.5 (UpstreamBuild, CodeChange)             
            3 ╓  CKt-PerfectEvent              v0.3.2      → 🡡/v0.3.3--ci.5 (UpstreamBuild, CodeChange)             
            4 ║  CKt-Monitoring                v0.2.3      → 🡡/v0.2.4--ci.5 (UpstreamBuild, CodeChange)             
            5 ╙  Samples/CKt-App-Sample        v0.0.0+fake → 🡡/v0.0.0--ci.2 (UpstreamBuild, FakeVersion, CodeChange)
            6 -  Samples/CKt-Sample-Monitoring v0.0.0+fake → 🡡/v0.0.0--ci.2 (UpstreamBuild, FakeVersion, CodeChange)
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱

            """ );

        // Everything has been built but nothing has been published.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      🡡/v1.0.1--ci.4
            -  CKt-ActivityMonitor           🡡/v0.1.1--ci.5
            ╓  CKt-PerfectEvent              🡡/v0.3.3--ci.5
            ║  CKt-Monitoring                🡡/v0.2.4--ci.5
            ╙  Samples/CKt-App-Sample        🡡/v0.0.0--ci.2
            -  Samples/CKt-Sample-Monitoring 🡡/v0.0.0--ci.2
            There is nothing to build across the 6 repositories.
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );


        // Now we publish.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      🡡/v1.0.1--ci.4
            -  CKt-ActivityMonitor           🡡/v0.1.1--ci.5
            ╓  CKt-PerfectEvent              🡡/v0.3.3--ci.5
            ║  CKt-Monitoring                🡡/v0.2.4--ci.5
            ╙  Samples/CKt-App-Sample        🡡/v0.0.0--ci.2
            -  Samples/CKt-Sample-Monitoring 🡡/v0.0.0--ci.2
            There is nothing to build across the 6 repositories.
            🡡 6 repositories must be published.
            ❰✓❱
            
            """ );

        var (nugetOrgFeed, sosFeed) = Helper.GetFakeFeedPaths( clonedFolder.Path );

        // CI build: nuget.org is not concerned: out fake nuget.org oly contains the canary package.
        Directory.GetDirectories( nugetOrgFeed ).Select( p => Path.GetFileName( p ) ).ShouldBe( ["ck.canarypackage"] );

        // The other feed has the packages.
        var existingPackages = Directory.GetDirectories( sosFeed )
                                        .SelectMany( p => Directory.GetDirectories( p )
                                                                   .Select( pp => new PackageInstance( Path.GetFileName( p ),
                                                                                                       SVersion.Parse( Path.GetFileName( pp ) ) ) ) );
        existingPackages.Select( p => p.ToString() )
                        .ShouldBe( ["ck.canarypackage@1.0.0",
                                    "ckt.activitymonitor@0.1.1--ci.5",
                                    "ckt.core@1.0.1--ci.4",
                                    "ckt.monitoring@0.2.4--ci.5",
                                    "ckt.perfectevent@0.3.3--ci.5",
                                    "ckt.sample.monitoring@0.0.0--ci.2",
                                    "ckt.someapp@0.0.0--ci.2"], ignoreOrder: true );

        // The FileSystemHostingProvider received the asset files.
        var appRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-App-Sample", "Releases" );
        Directory.GetFiles( appRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.0--ci.2/ZipDemo.zip"] );

        var sampleRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-Sample-Monitoring", "Releases" );
        Directory.GetFiles( sampleRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.0--ci.2/Install-0.0.0--ci.2.txt"] );

        // The "PublishState.bin" has been removed.
        File.Exists( context.CurrentStackPath.Combine( "$Local/PublishState.bin" ) ).ShouldBeFalse();

        // No issue.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );
    }

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_add_sample_to_with_sample_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(with_sample)" ) );
        await CKt_add_sample_and_ci_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_add_sample_and_ci_Async", "CKt", "(with_sample)" );
    }


}
