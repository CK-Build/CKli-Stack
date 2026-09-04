using CK.Core;
using CKli;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests.Integration;

public class S1ᅳInitializedᅳTests
{
    [Explicit]
    [TestCase( "", "" )]
    [TestCase( "PublishCI", "" )]
    [TestCase( "", "PublishCI" )]
    [TestCase( "PublishCI", "PublishCI" )]
    public async Task adding_a_GitHub_remote_repo_Async( string ciFirstMode, string ciSecondMode )
    {
        TestHelper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        bool firstModeCI = ciFirstMode == "PublishCI";
        bool secondModeCI = ciSecondMode == "PublishCI";

        var gitHubProvider = Helper.GetGitHubHostingProvider();
        await gitHubProvider.DeleteRepositoryAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create" ).ConfigureAwait( false );

        try
        {
            // Add the remote repository to the fully local stack.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "create", "https://github.com/CK-Build/Test-Repo-Create" ).ConfigureAwait( false )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            > Test-Repo-Create (2)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by creating a 'v0.0.0+fake' on 'stable' branch.
            ❰✓❱

            """ );
            var testRepo = context.ChangeDirectory( "Test-Repo-Create" );
            Directory.Exists( testRepo.CurrentDirectory ).ShouldBeTrue();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "branch", "switch", "dev/stable" ).ConfigureAwait( false )).ShouldBeTrue();
            var path = testRepo.CurrentDirectory.AppendPart( "SomeApp" );
            Directory.CreateDirectory( path );
            File.WriteAllText( path.AppendPart( "SomeApp.csproj" ), """
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                </PropertyGroup>
            </Project>

            """ );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "exec", "dotnet", "new", "sln" ).ConfigureAwait( false )).ShouldBeTrue();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "exec", "dotnet", "sln", "add", "SomeApp/SomeApp.csproj" ).ConfigureAwait( false )).ShouldBeTrue();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "commit", "Added project." ).ConfigureAwait( false )).ShouldBeTrue();
            display.Clear();
            if( firstModeCI )
            {
                display.Clear();
                (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "publish", "--ci" ).ConfigureAwait( false )).ShouldBeTrue();
                display.ToString().ShouldBe( """
                      ╓      CKt-Core            v1.0.0     
                    1 ╙  ⊙   Test-Repo-Create    v0.0.0+fake → ⏚/v0.0.0--ci.2 (FakeVersion, CodeChange)
                      -      CKt-ActivityMonitor v0.1.0     
                      ╓      CKt-PerfectEvent    v0.3.2     
                      ╙      CKt-Monitoring      v0.2.3     
                    Required build for 1 from the single pivot out of 5 repositories and 1 can be published.
                    (No dependency updates other than the ones from the upstreams are needed.)
                    ❰✓❱

                    """ );
            }
            else
            {
                display.Clear();
                (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "publish" ).ConfigureAwait( false )).ShouldBeTrue();
                display.ToString().ShouldBe( """
                      ╓      CKt-Core            v1.0.0     
                    1 ╙  ⊙   Test-Repo-Create    v0.0.0+fake → ⏚/v0.0.0 (FakeVersion, CodeChange)
                      -      CKt-ActivityMonitor v0.1.0     
                      ╓      CKt-PerfectEvent    v0.3.2     
                      ╙      CKt-Monitoring      v0.2.3     
                    Required build for 1 from the single pivot out of 5 repositories and 1 can be published.
                    (No dependency updates other than the ones from the upstreams are needed.)
                    ❰✓❱

                    """ );
            }
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "branch", "switch", "dev/stable" ).ConfigureAwait( false )).ShouldBeTrue();
            TestHelper.TouchAndCommit( testRepo.CurrentDirectory, "dev/stable" );

            // Don't handle bool result: git branch fails when the branch already exists.
            await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "exec", "git", "branch", "test-branch" ).ConfigureAwait( false );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "branch", "push", "test-branch" ).ConfigureAwait( false )).ShouldBeTrue();
            (await gitHubProvider.SetDefaultBranchAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create", "test-branch" ).ConfigureAwait( false )).ShouldBeTrue();

            await Task.Delay( 200 );

            if( secondModeCI )
            {
                display.Clear();
                (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "publish", "--ci" ).ConfigureAwait( false )).ShouldBeTrue();
                if( firstModeCI )
                {
                    display.ToString().ShouldBe( """
                          ╓      CKt-Core            v1.0.0      
                        1 ╙  ⊙   Test-Repo-Create    v0.0.0--ci.2 → ⏚/v0.0.0--ci.3 (CodeChange)
                          -      CKt-ActivityMonitor v0.1.0      
                          ╓      CKt-PerfectEvent    v0.3.2      
                          ╙      CKt-Monitoring      v0.2.3      
                        Required build for 1 from the single pivot out of 5 repositories and 1 can be published.
                        (No dependency updates other than the ones from the upstreams are needed.)
                        ❰✓❱

                        """ );
                }
                else
                {
                    display.ToString().ShouldBe( """
                          ╓      CKt-Core            v1.0.0
                        1 ╙  ⊙   Test-Repo-Create    v0.0.0 → ⏚/v0.0.1--ci.1 (CodeChange)
                          -      CKt-ActivityMonitor v0.1.0
                          ╓      CKt-PerfectEvent    v0.3.2
                          ╙      CKt-Monitoring      v0.2.3
                        Required build for 1 from the single pivot out of 5 repositories and 1 can be published.
                        (No dependency updates other than the ones from the upstreams are needed.)
                        ❰✓❱

                        """ );
                }
            }
            else
            {
                display.Clear();
                (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "publish" ).ConfigureAwait( false )).ShouldBeTrue();
                if( firstModeCI )
                {
                    display.ToString().ShouldBe( """
                          ╓      CKt-Core            v1.0.0     
                        1 ╙  ⊙   Test-Repo-Create    v0.0.0+fake → ⏚/v0.0.0 (FakeVersion, CodeChange)
                          -      CKt-ActivityMonitor v0.1.0     
                          ╓      CKt-PerfectEvent    v0.3.2     
                          ╙      CKt-Monitoring      v0.2.3     
                        Required build for 1 from the single pivot out of 5 repositories and 1 can be published.
                        (No dependency updates other than the ones from the upstreams are needed.)
                        ❰✓❱

                        """ );
                }
                else
                {
                    display.ToString().ShouldBe( """
                          ╓      CKt-Core            v1.0.0
                        1 ╙  ⊙   Test-Repo-Create    v0.0.0 → ⏚/v0.0.1 (CodeChange)
                          -      CKt-ActivityMonitor v0.1.0
                          ╓      CKt-PerfectEvent    v0.3.2
                          ╙      CKt-Monitoring      v0.2.3
                        Required build for 1 from the single pivot out of 5 repositories and 1 can be published.
                        (No dependency updates other than the ones from the upstreams are needed.)
                        ❰✓❱

                        """ );
                }
            }


            // Test that dev/stable being the default branch is handled.
            // Don't handle bool result: git branch fails when the branch already exists.
            await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "exec", "git", "branch", "dev/stable" ).ConfigureAwait( false );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "branch", "push", "dev/stable" ).ConfigureAwait( false )).ShouldBeTrue();

            (await gitHubProvider.SetDefaultBranchAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create", "dev/stable" ).ConfigureAwait( false )).ShouldBeTrue();
            var info = await gitHubProvider.GetRepositoryInfoAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create", true ).ConfigureAwait( false );
            while( info.ShouldNotBeNull().DefaultBranch != "dev/stable" )
            {
                await Task.Delay( 200 );
                info = await gitHubProvider.GetRepositoryInfoAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create", true ).ConfigureAwait( false );
            }
            // Touch and publish: Non-CI publication here (a CI publication would let the dev/stable).
            // => We publish "stable" here: it MUST be restored as the default branch (because it is the BranchModel's root "stable").
            TestHelper.TouchAndCommit( testRepo.CurrentDirectory, "dev/stable" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, testRepo, "publish" ).ConfigureAwait( false )).ShouldBeTrue();

            info = await gitHubProvider.GetRepositoryInfoAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create", true ).ConfigureAwait( false );
            while( info.ShouldNotBeNull().DefaultBranch != "stable" )
            {
                await Task.Delay( 200 );
                info = await gitHubProvider.GetRepositoryInfoAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create", true ).ConfigureAwait( false );
            }
        }
        finally
        {
            await gitHubProvider.DeleteRepositoryAsync( TestHelper.Monitor, "CK-Build/Test-Repo-Create" ).ConfigureAwait( false );
        }

    }


    /// <summary>
    /// <see cref="CKli.HotZone.Plugin.HotZonePlugin.FixStartAsync"/>
    /// <see cref="CKli.HotZone.Plugin.HotZonePlugin.FixInfo"/>
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuildAsync"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task local_fix_Async()
    {
        TestHelper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
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
            // The first (CKt-Core) is ci.2 (Empty commit + "Net8 migration applied") and the following ci.3 because of
            // Empty Commit + "Net8 migration applied" + Update dependencies.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build", "--ci" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
                  CKt-Core            ⎇ fix/v1.0  → v1.0.1--ci.2
                  CKt-ActivityMonitor ⎇ fix/v0.1  → v0.1.1--ci.3
                  CKt-PerfectEvent    ⎇ fix/v0.2  → v0.2.2--ci.3
                  CKt-PerfectEvent    ⎇ fix/v0.3  → v0.3.3--ci.3
                  CKt-Monitoring      ⎇ fix/v0.2  → v0.2.4--ci.3
                ❰✓❱

                """ );

        }

        // ckli fix build --ci
        using( TestHelper.Monitor.OpenInfo( "Second 'ckli fix build --ci' (no change), all are skipped." ) )
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build", "--ci" )).ShouldBeTrue();
            logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1--ci.2' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-ActivityMonitor/0.1.1--ci.3' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.2.2--ci.3' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.3.3--ci.3' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-Monitoring/0.2.4--ci.3' skipped." );
            display.ToString().ShouldBe( """
                  CKt-Core            ⎇ fix/v1.0    v1.0.1--ci.2
                  CKt-ActivityMonitor ⎇ fix/v0.1    v0.1.1--ci.3
                  CKt-PerfectEvent    ⎇ fix/v0.2    v0.2.2--ci.3
                  CKt-PerfectEvent    ⎇ fix/v0.3    v0.3.3--ci.3
                  CKt-Monitoring      ⎇ fix/v0.2    v0.2.4--ci.3
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
              CKt-Core            ⎇ fix/v1.0  → v1.0.2--ci.1
              CKt-ActivityMonitor ⎇ fix/v0.1  → v0.1.2--ci.2
              CKt-PerfectEvent    ⎇ fix/v0.2  → v0.2.3--ci.2
              CKt-PerfectEvent    ⎇ fix/v0.3  → v0.3.4--ci.2
              CKt-Monitoring      ⎇ fix/v0.2  → v0.2.5--ci.3
            ❰✓❱

            """ );

        // No build required...
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              CKt-Core            ⎇ fix/v1.0    v1.0.2--ci.1
              CKt-ActivityMonitor ⎇ fix/v0.1    v0.1.2--ci.2
              CKt-PerfectEvent    ⎇ fix/v0.2    v0.2.3--ci.2
              CKt-PerfectEvent    ⎇ fix/v0.3    v0.3.4--ci.2
              CKt-Monitoring      ⎇ fix/v0.2    v0.2.5--ci.3
            ❰✓❱

            """ );

        display.Clear();
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
    public async Task CKt_add_sample_and_ci_Async()
    {
        TestHelper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        var inSampleFolder = context.ChangeDirectory( "Samples" );

        var newRepo1 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-Sample-Monitoring" );
        var newRepoUrl1 = $"file://{newRepo1}";
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "create", newRepoUrl1 )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > Samples/CKt-Sample-Monitoring (2)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by creating a 'v0.0.0+fake' on 'stable' branch.
            ❰✓❱

            """ );

        var newRepo2 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-App-Sample" );
        var newRepoUrl2 = $"file://{newRepo2}";
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "create", newRepoUrl2 )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > Samples/CKt-App-Sample (2)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by creating a 'v0.0.0+fake' on 'stable' branch.
            ❰✓❱

            """ );

        // The issues are in the other (old) repos! (The local fake feed paths have changed.)
        // The "ckli repo create" has "ckli issue --fix" in the CKt-App-Sample.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            ❰✓❱

            """ );

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

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "commit", "Setup the CKt.Sample.Monitoring repo." )).ShouldBeTrue();

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

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "commit", "Setup the CKt-App-Sample repo." )).ShouldBeTrue();
        }
        #endregion


        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "switch", "dev/stable" )).ShouldBeTrue();

        // => Creates the missing nuget.config file and updated the feed urls in existing ones:
        //    this is the work of the ArtifactHandlerPlugin plugin and the BranchModel/HotBranch/ContentIssue.
        //    The "Missing initial version." also doesn't require a clean working folder.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            ❰✓❱

            """ );
        // This will fix and commit.
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
            1 -  CKt-Core                      v1.0.0      → ⏚/v1.0.1--ci.4 (CodeChange)                            
            2 -  CKt-ActivityMonitor           v0.1.0      → ⏚/v0.1.1--ci.5 (UpstreamBuild, CodeChange)             
            3 ╓  CKt-PerfectEvent              v0.3.2      → ⏚/v0.3.3--ci.5 (UpstreamBuild, CodeChange)             
            4 ║  CKt-Monitoring                v0.2.3      → ⏚/v0.2.4--ci.5 (UpstreamBuild, CodeChange)             
            5 ╙  Samples/CKt-App-Sample        v0.0.0+fake → ⏚/v0.0.0--ci.3 (UpstreamBuild, FakeVersion, CodeChange)
            6 -  Samples/CKt-Sample-Monitoring v0.0.0+fake → ⏚/v0.0.0--ci.3 (UpstreamBuild, FakeVersion, CodeChange)
            Required build for 6 repositories across the 6 repositories and 6 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Everything has been built but nothing has been published.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      ⏚/v1.0.1--ci.4
            -  CKt-ActivityMonitor           ⏚/v0.1.1--ci.5
            ╓  CKt-PerfectEvent              ⏚/v0.3.3--ci.5
            ║  CKt-Monitoring                ⏚/v0.2.4--ci.5
            ╙  Samples/CKt-App-Sample        ⏚/v0.0.0--ci.3
            -  Samples/CKt-Sample-Monitoring ⏚/v0.0.0--ci.3
            There is nothing to build across the 6 repositories but 6 can be published.
            ❰✓❱
            
            """ );


        // Now we publish.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      ⏚/v1.0.1--ci.4
            -  CKt-ActivityMonitor           ⏚/v0.1.1--ci.5
            ╓  CKt-PerfectEvent              ⏚/v0.3.3--ci.5
            ║  CKt-Monitoring                ⏚/v0.2.4--ci.5
            ╙  Samples/CKt-App-Sample        ⏚/v0.0.0--ci.3
            -  Samples/CKt-Sample-Monitoring ⏚/v0.0.0--ci.3
            There is nothing to build across the 6 repositories but 6 can be published.
            ❰✓❱
            
            """ );

        var (nugetOrgFeed, sosFeed) = Helper.GetFakeFeedPaths( clonedFolder.Path );

        // CI build: nuget.org is not concerned: our fake nuget.org oly contains the canary package.
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
                                    "ckt.sample.monitoring@0.0.0--ci.3",
                                    "ckt.someapp@0.0.0--ci.3"], ignoreOrder: true );

        // The FileSystemHostingProvider received the asset files.
        var appRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-App-Sample", "Releases" );
        Directory.GetFiles( appRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.0--ci.3/ZipDemo.zip"] );

        var sampleRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-Sample-Monitoring", "Releases" );
        Directory.GetFiles( sampleRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.0--ci.3/Install-0.0.0--ci.3.txt"] );

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
    public async Task restoring_from_private_feed_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        // Must alter the definition file before creating the repo (configured NuGetFeeds are cached).
        //
        // This feed is private read-only feed (because it has no PushQualityFilter).
        // 
        NormalizedPath definitionPath = world.WorldRoot.CurrentStackPath.AppendPart( "Test.xml" );
        var definitionFile = XElement.Load( definitionPath );
        definitionFile.Descendants( "NuGet" ).Single().AddFirst(
            new XElement( "Feed", new XAttribute( "Name", "SC" ), new XAttribute( "Url", "https://pkgs.dev.azure.com/Signature-Code/_packaging/Default/nuget/v3/index.json" ),
                        new XElement( "Credentials", new XAttribute( "SecretKey", "SC_READ_PAT" ) ) ) );
        definitionFile.Descendants( "Feed" )
                        .Single( e => e.Attribute( "Name" )!.Value == "NuGet" ).SetAttributeValue( "Url", "https://api.nuget.org/v3/index.json" );

        definitionFile.SafeSave( definitionPath );

        // This test uses the real "dotnet build".
        BuildPlugin.SetBuilderFunction( null );

        // Creates a repository with a default project that has a PackageReference to a package that exists ONLY in the private feed.
        await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        NormalizedPath projectPath = world.WorldRoot.CurrentDirectory.Combine( "X-Core/X.Core/X.Core.csproj" );
        var project = XElement.Load( projectPath );
        project.Descendants( "ItemGroup" ).First().Add(
            new XElement( "PackageReference", new XAttribute( "Include", "SLog.Device.Signature" ), new XAttribute( "Version", "13.2.0" ) ) );
        project.SafeSave( projectPath );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "commit", "Added ref to SC feed package only." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--ci" )).ShouldBeTrue();
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
