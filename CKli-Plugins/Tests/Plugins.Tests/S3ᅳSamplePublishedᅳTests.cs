using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public partial class S3ᅳSamplePublishedᅳTests
{
    [TestCase( true )]
    [TestCase( false )]
    public async Task coworking_Async( bool useCheckout )
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );

        // Shares the "FakeFeed/" folder in the cloned folder.
        var bob = await remotes.CloneAsync( clonedFolder.Path.AppendPart( "Bob" ),
                                            allowDuplicateStack: false,
                                            ( monitor, stackPath, plugins )
                                                => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                               .ConfigureAwait( false );
        var bobDisplay = (StringScreen)bob.Screen;


        // Bob and Tim have no issues except the "nuget.config" files that reference the "CKt_publish_with_sample-NonPackableSample/FakeFeed"
        // instead of the local "coworking_Async/FakeFeed" shared folder.
        // We may not fix these issues because the BuildPlugin checks and updates the "nuget.config" file just
        // before compiling (and restores it after the build), but here we fix these issues. We use Bob to fix and publishes the new updated
        // versions. Then Tim clones its repository.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
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
                > Samples/CKt-Sample-Monitoring (1)
                │ > Content issues.
                │ │ Branch: stable (1 content issue)
                │ │ > File 'nuget.config' must be updated.
                > Samples/CKt-App-Sample (1)
                │ > Content issues.
                │ │ Branch: stable (1 content issue)
                │ │ > File 'nuget.config' must be updated.
                ❰✓❱

                """ );
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue", "--fix" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
            ❰✓❱
            
            """ );
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.1 → ⏚/v1.0.2 (CodeChange)               
            2 -  CKt-ActivityMonitor           v0.1.1 → ⏚/v0.1.2 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4 (UpstreamBuild, CodeChange)
            4 ║  CKt-Monitoring                v0.2.4 → ⏚/v0.2.5 (UpstreamBuild, CodeChange)
            5 ╙  Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1 (UpstreamBuild, CodeChange)
            6 -  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1 (UpstreamBuild, CodeChange)
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱
            
            """ );

        // Tim clones a repository without any issue.
        var tim = await remotes.CloneAsync( clonedFolder.Path.AppendPart( "Tim" ),
                                            allowDuplicateStack: true,
                                            ( monitor, stackPath, plugins ) => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                               .ConfigureAwait( false );
        var timDisplay = (StringScreen)tim.Screen;

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "issue" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
                ❰✓❱

                """ );

        // Bob and Tim have the same status (here Tim only is tested).
        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "status" )).ShouldBeTrue();
        timDisplay.ToString().Replace( TestHelper.CKliStackWorkingFolder, "<Stack>" ).ShouldBe( """
            > Public stack CKt (6 repositories)
            │  <Stack>/CKli-Plugins/Tests/Plugins.Tests/Cloned/coworking_Async/Tim/DuplicateOf-CKt/.PublicStack
            │  file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Stack
              CKt-Core                      ⎇ stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Core              
              CKt-ActivityMonitor           ⎇ stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-ActivityMonitor   
              CKt-PerfectEvent              ⎇ stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-PerfectEvent      
              CKt-Monitoring                ⎇ stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Monitoring        
              Samples/CKt-Sample-Monitoring ⎇ stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Sample-Monitoring 
              Samples/CKt-App-Sample        ⎇ stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-App-Sample        
            ❰✓❱

            """ );

        // Bob starts to work on CK-PerfectEvent: he creates the "dev/stable" branch and adds a commit (with a breaking change).
        var bobPerfectEvent = bob.ChangeDirectory( "CKt-PerfectEvent" );
        await TouchDevStableAsync( bobPerfectEvent,
                                   useCheckout,
                                   "fix!: This is a breaking change because of the exclamation mark.",
                                   "Bob-work.txt" );

        // Tim publishes a new non-CI version of CK-PerfectEvent (no specific commit message: a fix).
        var timPerfectEvent = tim.ChangeDirectory( "CKt-PerfectEvent" );
        await TouchDevStableAsync( timPerfectEvent,
                                   useCheckout,
                                   fileName: "Tim-work.txt" );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "publish" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.2
              - →·   CKt-ActivityMonitor           v0.1.2
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → ⏚/v0.3.5 (CodeChange)   
              ║      CKt-Monitoring                v0.2.5
              ╙      Samples/CKt-App-Sample        v0.0.1
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → ⏚/v0.0.2 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Bob ckli pulls. Its "dev/stable" is now no more a tracking branch because the "refs/remotes/origin/dev/stable"
        // branch has been pruned.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();

        /// Bob wants its "Bob-work.txt" contribution to be published, but he cannot (either in CI or in non-CI):
        /// first it must incorporate Tim's work.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "publish", "--ci", "-d" )).ShouldBeFalse();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "publish", "-d" )).ShouldBeFalse();

        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
            > CKt-PerfectEvent (1)
            │ > Desynchronized branches.
            │ │ - Branch 'stable' has 1 commits that must be in 'dev/stable'.
            │ │ Base branches can be merged without conflict into the desynchronized branches.
            ❰✓❱

            """ );

        // Tim publishes a fix of the version he just published (but in CI) of CK-PerfectEvent.
        await TouchDevStableAsync( timPerfectEvent,
                                   useCheckout,
                                   fileName: "Tim-work.txt" );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "publish", "--ci" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.2
              - →·   CKt-ActivityMonitor           v0.1.2
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.5 → ⏚/v0.3.6--ci.1 (CodeChange)   
              ║      CKt-Monitoring                v0.2.5
              ╙      Samples/CKt-App-Sample        v0.0.1
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.2 → ⏚/v0.0.3--ci.1 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Bob ckli pulls. Its "dev/stable" is tracking the "refs/remotes/origin/dev/stable" (because Tim has pushed in ci).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();

        // Bob doesn't need to synchronize the "stable' branch into the "dev/stable": the pull dit it because the remote "dev/stable" now exists.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
        ❰✓❱

        """ );

        // Bob publishes a non CI version here (with its "Bob-work.txt" contribution that is a "fix!:" => breaking change but
        // since we are in 0.X.Y version, only the Minor is incremented). From CK-PerfectEvent, the change propagates to the
        // CK-Sample-Monitoring.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "publish" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.2
              - →·   CKt-ActivityMonitor           v0.1.2
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.5 → ⏚/v0.4.0 (CodeChange)               
              ║      CKt-Monitoring                v0.2.5
              ╙      Samples/CKt-App-Sample        v0.0.1
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.2 → ⏚/v0.1.0 (UpstreamBuild, CodeChange)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Tim ckli pulls, but before he creates the dev/stable branch. This is useless and the
        // issue below reflects this.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "branch", "switch", "dev/stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "pull" )).ShouldBeTrue();

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "issue" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
        > CKt-PerfectEvent (1)
        │ > Removable branches.
        │ │ - 'dev/stable' is merged into 'stable'.
        │ │ It can be deleted.
        ❰✓❱

        """ );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "issue", "--fix" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
        ❰✓❱

        """ );
    }

    static async Task TouchDevStableAsync( CKliEnv context, bool useCheckout, string? commitMessage = null, string fileName = "CKliTouchAndCommit.txt" )
    {
        if( useCheckout )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        }
        else
        {
            // No way in one call to git :-(
            // Solution 1: git branch <branch-name> 2>/dev/null || true
            // Solution 2: git show-ref --verify --quiet refs/heads/<branch-name> || git branch <branch-name >
            // Solution 3: git rev-parse --verify <branch-name> >/dev/null 2>&1 || git branch <branch-name>
            //
            // => We "git branch dev/stable", ignoring the error.
            await CKliCommands.ExecAsync( TestHelper.Monitor, context, "exec", "git", "branch", "dev/stable" );
        }
        TestHelper.TouchAndCommit( context.CurrentDirectory,
                                   branchName: "dev/stable",
                                   commitMessage: commitMessage,
                                   fileName: fileName );
    }


    [Test]
    public async Task with_deprecation_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        // Let's deprecate the current CKt-PerfectEvent v0.3.3 package (in 30.days).
        // => This propagates to downstream repositories (here: Samples/CKt-Sample-Monitoring).
        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "version", "deprecate", "v0.3.3", "--days", "30", "--reason", "For fun." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1           
              - →·   CKt-ActivityMonitor           v0.1.1           
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3+deprecated → ⏚/v0.3.4 (DeprecatedVersion)               
              ║      CKt-Monitoring                v0.2.4           
              ╙      Samples/CKt-App-Sample        v0.0.0           
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+deprecated → ⏚/v0.0.1 (UpstreamBuild, DeprecatedVersion)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Deprecate it now!
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "version", "deprecate", "v0.3.3", "--immediate", "--allow-update" )).ShouldBeTrue();

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
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be updated.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1           
              - →·   CKt-ActivityMonitor           v0.1.1           
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3+deprecated → ⏚/v0.3.4 (DeprecatedVersion)               
              ║      CKt-Monitoring                v0.2.4           
              ╙      Samples/CKt-App-Sample        v0.0.0           
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+deprecated → ⏚/v0.0.1 (UpstreamBuild, DeprecatedVersion)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }

    [Test]
    public async Task fake_version_sets_the_version_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        // Let's deprecate the current CKt-PerfectEvent v0.3.3 package (in 30.days).
        // => This propagates to downstream repositories (here: Samples/CKt-Sample-Monitoring).
        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "exec", "git", "tag", "v5.4.3+fake" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1     
              - →·   CKt-ActivityMonitor           v0.1.1     
            1 ╓  ⊙   CKt-PerfectEvent              v5.4.3+fake → ⏚/v5.4.3 (FakeVersion)  
              ║      CKt-Monitoring                v0.2.4     
              ╙      Samples/CKt-App-Sample        v0.0.0     
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0      → ⏚/v0.0.1 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

    }

    [TestCase( true )]
    [TestCase( false )]
    public async Task ci_followed_by_non_ci_Async( bool useCheckout )
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        var activityMonitor = context.ChangeDirectory( "CKt-ActivityMonitor" );
        var perfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        await TouchDevStableAsync( perfectEvent, useCheckout );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  CKt-Core                      v1.0.1
              -  CKt-ActivityMonitor           v0.1.1
            1 ╓  CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4--ci.1 (CodeChange)   
              ║  CKt-Monitoring                v0.2.4
              ╙  Samples/CKt-App-Sample        v0.0.0
            2 -  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 2 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  CKt-Core                      v1.0.1
              -  CKt-ActivityMonitor           v0.1.1
            1 ╓  CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4 (CodeChange)               
              ║  CKt-Monitoring                v0.2.4
              ╙  Samples/CKt-App-Sample        v0.0.0
            2 -  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1 (UpstreamBuild, CodeChange)
            Required build for 2 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        await TouchDevStableAsync( activityMonitor, useCheckout );
        await TouchDevStableAsync( perfectEvent, useCheckout );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "build", "-d" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → ⏚/v0.3.5 (CodeChange)   
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → ⏚/v0.0.2 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "*build", "-d" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
            1 - →·   CKt-ActivityMonitor           v0.1.1 → ⏚/v0.1.2 (CodeChange)               
            2 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → ⏚/v0.3.5 (UpstreamBuild, CodeChange)
            3 ║      CKt-Monitoring                v0.2.4 → ⏚/v0.2.5 (UpstreamBuild)            
            4 ╙      Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1 (UpstreamBuild)            
            5 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → ⏚/v0.0.2 (UpstreamBuild)            
            Required build for 5 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );


    }


    [Test]
    public async Task common_files_tests_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        var ckliRoot = TestHelper.SolutionFolder.RemoveLastPart().AppendPart( "CKli" );

        var commonFolder = context.CurrentStackPath.AppendPart( "Common" );
        Directory.CreateDirectory( commonFolder );

        var globalJsonContent = File.ReadAllText( ckliRoot.AppendPart( "global.json" ) );
        File.WriteAllText( commonFolder.AppendPart( "global.json" ), globalJsonContent );

        var directoryPropsContent = File.ReadAllText( ckliRoot.AppendPart( "Directory.Build.props" ) );
        File.WriteAllText( commonFolder.AppendPart( "Directory.Build.props" ), directoryPropsContent );

        // The "real" .slnx file has no impact here as all the .slnx exist already.
        File.WriteAllText( commonFolder.AppendPart( "[InitOnly]$RepositoryName$.slnx" ), "<Solution></Solution>" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: stable (3 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: stable (3 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: stable (3 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (3 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (3 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.props
            │ │ - global.json
            │ │ > File 'nuget.config' must be updated.
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: stable (3 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.props
            │ │ - global.json
            │ │ > File 'nuget.config' must be updated.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱
            
            """ );

        var initOnlyFilePath = commonFolder.AppendPart( "[InitOnly] JustForTest.txt" );
        File.WriteAllText( initOnlyFilePath, "Hello" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            ❰✓❱

            """ );

        // Let's fix for CKt-Core
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context.ChangeDirectory( "CKt-Core" ), "issue", "--fix" )).ShouldBeTrue();

        // Test with different case and space.
        File.Delete( initOnlyFilePath );
        initOnlyFilePath = commonFolder.AppendPart( " [ iNItonlY ]Justfortest.txt" );
        File.WriteAllText( initOnlyFilePath, "Hello" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ > Branch: stable (1 content issue)
            │ │ │ > File must be moved: JustForTest.txt → Justfortest.txt (case differ)
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            ❰✓❱

            """ );

        // Use CKt-App-Sample to test the build as it is packable.
        var inAppSample = context.ChangeDirectory( "Samples/CKt-App-Sample" );

        // With the "standard" Directory.Build.props:
        // - <Project>/Doc/Package.md is required in any packable project.
        // - Common/Package.icon is required when packing.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "branch", "switch", "dev/stable" )).ShouldBeTrue();

        Directory.CreateDirectory( inAppSample.CurrentDirectory.Combine( "CKt.SomeApp/docs" ) );
        File.WriteAllText( inAppSample.CurrentDirectory.Combine( "CKt.SomeApp/docs/Package.md" ), """
            This package is a fake to test [CKli](https://github.com/CK-Build/CKli).
            """ );

        Directory.CreateDirectory( inAppSample.CurrentDirectory.Combine( "Common" ) );
        File.Copy( ckliRoot.Combine( "Common/PackageIcon.png" ), inAppSample.CurrentDirectory.Combine( "Common/PackageIcon.png" ) );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "commit", "Added package doc." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
              ╓      CKt-PerfectEvent              v0.3.3
              ║      CKt-Monitoring                v0.2.4
            1 ╙  ⊙   Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1 (CodeChange)
              -      Samples/CKt-Sample-Monitoring v0.0.0
            Required build for 1 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱
            
            """ );

    }

    [Test]
    public async Task with_ci_0_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        // This test publishes.
        Helper.SetFileSystemWritePAT();

        // The ci.0 is generated for all unchanged repos... But only rank 0 repositories are actually concerned
        // (the ones without upstreams) can be CI0 because the other ones are de facto UpstreamBuild.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.1 → ⏚/v1.0.2--ci.0 (CI0)          
            2 -  CKt-ActivityMonitor           v0.1.1 → ⏚/v0.1.2--ci.1 (UpstreamBuild)
            3 ╓  CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4--ci.1 (UpstreamBuild)
            4 ║  CKt-Monitoring                v0.2.4 → ⏚/v0.2.5--ci.1 (UpstreamBuild)
            5 ╙  Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            6 -  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // From CKt-PerfectEvent, a simple build ensures that the pivots will be in CI.
        // Their downstream repositories will also be in CI but not in ci.0 (regular propagation).
        var perfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4--ci.0 (CI0)          
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // When using *build (the "upstream closure build"), then we can be sure that all pivots final code will be in CI.
        // => We publish this.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "*build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.1 → ⏚/v1.0.2--ci.0 (CI0)          
            2 - →·   CKt-ActivityMonitor           v0.1.1 → ⏚/v0.1.2--ci.1 (UpstreamBuild)
            3 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4--ci.1 (UpstreamBuild)
            4 ║      CKt-Monitoring                v0.2.4 → ⏚/v0.2.5--ci.1 (UpstreamBuild)
            5 ╙      Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            6 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 6 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // => We publish this.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "*publish", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.1 → ⏚/v1.0.2--ci.0 (CI0)          
            2 - →·   CKt-ActivityMonitor           v0.1.1 → ⏚/v0.1.2--ci.1 (UpstreamBuild)
            3 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4--ci.1 (UpstreamBuild)
            4 ║      CKt-Monitoring                v0.2.4 → ⏚/v0.2.5--ci.1 (UpstreamBuild)
            5 ╙      Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            6 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1--ci.1 (UpstreamBuild)
            Required build for 6 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Already published. Nothing to do in "--ci.0".
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "*publish", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1      
            - →·   CKt-ActivityMonitor           v0.1.2--ci.1
            ╓  ⊙   CKt-PerfectEvent              v0.3.4--ci.1
            ║      CKt-Monitoring                v0.2.5--ci.1
            ╙      Samples/CKt-App-Sample        v0.0.1--ci.1
            -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.1
            There is nothing to build from the 1 pivots out of 6 repositories.
            ❰✓❱

            """ );

        // Nothing to do also in "--ci".
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "*publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1      
            - →·   CKt-ActivityMonitor           v0.1.2--ci.1
            ╓  ⊙   CKt-PerfectEvent              v0.3.4--ci.1
            ║      CKt-Monitoring                v0.2.5--ci.1
            ╙      Samples/CKt-App-Sample        v0.0.1--ci.1
            -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.1
            There is nothing to build from the 1 pivots out of 6 repositories.
            ❰✓❱

            """ );

        // Once built, the commit for the UpstreamBuild exists... This is clearly noisy: there's no
        // change at all because the updates of the dependencies restores the exact same content as
        // the previously built/published version.
        // Unfortunately, to handle this, we must be sure that no source code other than the
        // dependencies version have changed.
        // TODO: This is currently not implemented.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  CKt-Core                      v1.0.1
            1 -  CKt-ActivityMonitor           v0.1.1 → ⏚/v0.1.2 (DependencyUpdate, CodeChange) 
                                                                 U CKt.Core: 1.0.2--ci.0 → 1.0.1
            2 ╓  CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4 (UpstreamBuild, CodeChange)    
            3 ║  CKt-Monitoring                v0.2.4 → ⏚/v0.2.5 (UpstreamBuild, CodeChange)    
            4 ╙  Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1 (UpstreamBuild, CodeChange)    
            5 -  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1 (UpstreamBuild, CodeChange)    
            Required build for 5 repositories across the 6 repositories.
            U 1 updates from upstreams (not using '*publish' here).
            ❰✓❱

            """ );

    }


    [Test]
    public async Task rebuilding_local_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        // This test publishes.
        Helper.SetFileSystemWritePAT();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      v1.0.1
            -  CKt-ActivityMonitor           v0.1.1
            ╓  CKt-PerfectEvent              v0.3.3
            ║  CKt-Monitoring                v0.2.4
            ╙  Samples/CKt-App-Sample        v0.0.0
            -  Samples/CKt-Sample-Monitoring v0.0.0
            There is nothing to build across the 6 repositories.
            ❰✓❱

            """ );

        // Touching CKt-PerfectEvent and build: local/v0.3.4 and local/v0.0.1.
        var perfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "branch", "switch", "dev/stable", "--create" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( perfectEvent.CurrentDirectory, "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4 (CodeChange)   
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // The "local/v0.3.4" (and "local/v0.0.1") must be "moved", we must not generate "local/v0.3.5"
        // (and "local/v0.0.2" of CKt-Sample-Monitoring) here.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( perfectEvent.CurrentDirectory, "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1  
              - →·   CKt-ActivityMonitor           v0.1.1  
            1 ╓  ⊙   CKt-PerfectEvent              (v0.3.4) → ⏚/v0.3.4 (CodeChange)   
              ║      CKt-Monitoring                v0.2.4  
              ╙      Samples/CKt-App-Sample        v0.0.0  
            2 -  ·→  Samples/CKt-Sample-Monitoring (v0.0.1) → ⏚/v0.0.1 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );


    }

    [Test]
    public async Task romeo_with_Full_on_Stable_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = await remotes.CloneAsync( clonedFolder, Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var display = (StringScreen)context.Screen;

        // Let's open "romeo" in CKt-PerfectEvent and build it: this propagates to downstream
        // repositories (here: Samples/CKt-Sample-Monitoring).

        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "status" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CKt-PerfectEvent ⎇ dev/romeo (untracked)" );

        // Building from the "romeo" has... nothing to build.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1
            - →·   CKt-ActivityMonitor           v0.1.1
            ╓  ⊙   CKt-PerfectEvent              v0.3.3
            ║      CKt-Monitoring                v0.2.4
            ╙      Samples/CKt-App-Sample        v0.0.0
            -  ·→  Samples/CKt-Sample-Monitoring v0.0.0
            There is nothing to build from the 1 pivots out of 6 repositories.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );

        // But if we ask for a --ci.0, then it works: a romeo prerelease must be produced and this
        // will create the romeo branch to appear in the downstream repositories (here CKt-Sample-Monitoring).
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4-romeo.0.ci.0 (CI0)          
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1-romeo.0.ci.1 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Instead of working with the ci.0 here, we touch the CKt-PerfectEvent to have fixed packages.
        TestHelper.TouchAndCommit( inPerfectEvent.CurrentDirectory, branchName: null );

        // We obtain 2 "real" first romeo prerelease packages.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.3.4-romeo (CodeChange)   
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.0.1-romeo (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Touch again to trigger a Minor change followed by 2 fixes: the "feat:" commit belongs to the "head commits"
        // of the TagCommitTree (and must be found).
        TestHelper.TouchAndCommit( inPerfectEvent.CurrentDirectory, branchName: null, commitMessage: "feat: Some feature." );
        TestHelper.TouchAndCommit( inPerfectEvent.CurrentDirectory, branchName: null, commitMessage: "fix 1" );
        TestHelper.TouchAndCommit( inPerfectEvent.CurrentDirectory, branchName: null, commitMessage: "fix 2" );

        // Time to really build the romeo packages.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.4.0-romeo (CodeChange)   
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.1.0-romeo (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Touch the CKt-ActivityMonitor (on "dev/stable").
        var inActivityMonitor = context.ChangeDirectory( "CKt-ActivityMonitor" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inActivityMonitor, "branch", "switch", "dev/stable" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inActivityMonitor, "status" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CKt-ActivityMonitor ⎇ dev/stable (untracked)" );
        TestHelper.TouchAndCommit( inActivityMonitor.CurrentDirectory, branchName: null, commitMessage: "fix in core." );

        // Nothing to build anymore from CKt-PerfectEvent.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1        
            - →·   CKt-ActivityMonitor           v0.1.1        
            ╓  ⊙   CKt-PerfectEvent              ⏚/v0.4.0-romeo
            ║      CKt-Monitoring                v0.2.4        
            ╙      Samples/CKt-App-Sample        v0.0.0        
            -  ·→  Samples/CKt-Sample-Monitoring ⏚/v0.1.0-romeo
            There is nothing to build from the 1 pivots out of 6 repositories.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );

        // Nothing to build anymore from CKt-PerfectEvent even in CI.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--ci", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1        
            - →·   CKt-ActivityMonitor           v0.1.1        
            ╓  ⊙   CKt-PerfectEvent              ⏚/v0.4.0-romeo
            ║      CKt-Monitoring                v0.2.4        
            ╙      Samples/CKt-App-Sample        v0.0.0        
            -  ·→  Samples/CKt-Sample-Monitoring ⏚/v0.1.0-romeo
            There is nothing to build from the 1 pivots out of 6 repositories.
            (Using '*build' may detect required builds in upstreams repositories.)
            ❰✓❱

            """ );

        // --ci.0 must be used to force CI versions.
        // Note that the ci versions replace the non-ci one (there's only one "local/" at a time per branch name).
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--ci.0", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1        
              - →·   CKt-ActivityMonitor           v0.1.1        
            1 ╓  ⊙   CKt-PerfectEvent              (v0.4.0-romeo) → ⏚/v0.4.0-romeo.0.ci.4 (CI0)          
              ║      CKt-Monitoring                v0.2.4        
              ╙      Samples/CKt-App-Sample        v0.0.0        
            2 -  ·→  Samples/CKt-Sample-Monitoring (v0.1.0-romeo) → ⏚/v0.1.0-romeo.0.ci.2 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // But if we *build, then the touched CKt-ActivityMonitor and its downstream are in romeo.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1        
            1 - →·   CKt-ActivityMonitor           v0.1.1         → ⏚/v0.1.2-romeo (CodeChange)   
            2 ╓  ⊙   CKt-PerfectEvent              (v0.4.0-romeo) → ⏚/v0.4.0-romeo (UpstreamBuild)
            3 ║      CKt-Monitoring                v0.2.4         → ⏚/v0.2.5-romeo (UpstreamBuild)
            4 ╙      Samples/CKt-App-Sample        v0.0.0         → ⏚/v0.0.1-romeo (UpstreamBuild)
            5 -  ·→  Samples/CKt-Sample-Monitoring (v0.1.0-romeo) → ⏚/v0.1.0-romeo (UpstreamBuild)
            Required build for 5 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // And if we *build --ci, then romeo ci versions can be created.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*build", "--ci", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1        
            1 - →·   CKt-ActivityMonitor           v0.1.1         → ⏚/v0.1.2-romeo.0.ci.0 (CodeChange)   
            2 ╓  ⊙   CKt-PerfectEvent              (v0.4.0-romeo) → ⏚/v0.4.0-romeo.0.ci.5 (UpstreamBuild)
            3 ║      CKt-Monitoring                v0.2.4         → ⏚/v0.2.5-romeo.0.ci.1 (UpstreamBuild)
            4 ╙      Samples/CKt-App-Sample        v0.0.0         → ⏚/v0.0.1-romeo.0.ci.1 (UpstreamBuild)
            5 -  ·→  Samples/CKt-Sample-Monitoring (v0.1.0-romeo) → ⏚/v0.1.0-romeo.0.ci.2 (UpstreamBuild)
            Required build for 5 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Let's run this "ckli *build --ci" but with a publication.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*build", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1        
            1 - →·   CKt-ActivityMonitor           v0.1.1         → ⏚/v0.1.2-romeo.0.ci.0 (CodeChange)   
            2 ╓  ⊙   CKt-PerfectEvent              (v0.4.0-romeo) → ⏚/v0.4.0-romeo.0.ci.5 (UpstreamBuild)
            3 ║      CKt-Monitoring                v0.2.4         → ⏚/v0.2.5-romeo.0.ci.1 (UpstreamBuild)
            4 ╙      Samples/CKt-App-Sample        v0.0.0         → ⏚/v0.0.1-romeo.0.ci.1 (UpstreamBuild)
            5 -  ·→  Samples/CKt-Sample-Monitoring (v0.1.0-romeo) → ⏚/v0.1.0-romeo.0.ci.2 (UpstreamBuild)
            Required build for 5 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // And now *build in non-CI.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
            1 - →·   CKt-ActivityMonitor           v0.1.1 → ⏚/v0.1.2-romeo (CodeChange)               
            2 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → ⏚/v0.4.0-romeo (UpstreamBuild, CodeChange)
            3 ║      CKt-Monitoring                v0.2.4 → ⏚/v0.2.5-romeo (UpstreamBuild, CodeChange)
            4 ╙      Samples/CKt-App-Sample        v0.0.0 → ⏚/v0.0.1-romeo (UpstreamBuild, CodeChange)
            5 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → ⏚/v0.1.0-romeo (UpstreamBuild, CodeChange)
            Required build for 5 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );


    }
}
