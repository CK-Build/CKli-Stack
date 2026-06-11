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
        var bob = remotes.Clone( clonedFolder.Path.AppendPart( "Bob" ),
                                 allowDuplicateStack: false,
                                 ( monitor, stackPath, plugins ) => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) );
        var bobDisplay = (StringScreen)bob.Screen;

        var tim = remotes.Clone( clonedFolder.Path.AppendPart( "Tim" ),
                                 allowDuplicateStack: true,
                                 ( monitor, stackPath, plugins ) => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) );
        var timDisplay = (StringScreen)tim.Screen;


        // Bob and Tim have no issues.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
        ❰✓❱

        """ );
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
              CKt-Core                      stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Core              
              CKt-ActivityMonitor           stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-ActivityMonitor   
              CKt-PerfectEvent              stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-PerfectEvent      
              CKt-Monitoring                stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Monitoring        
              Samples/CKt-Sample-Monitoring stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Sample-Monitoring 
              Samples/CKt-App-Sample        stable ↑0↓0 file:///<Stack>/CKli-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-App-Sample        
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
          - →·   CKt-Core                      v1.0.1
          - →·   CKt-ActivityMonitor           v0.1.1
        1 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → v0.3.4 🡡 (CodeChange)   
          ║      CKt-Monitoring                v0.2.4
          ╙      Samples/CKt-App-Sample        v0.0.0
        2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1 🡡 (UpstreamBuild)
        Required build for 2 from the 1 pivots out of 6 repositories.
        (No dependency updates other than the ones from the upstreams are needed.)
        🡡 2 repositories must be published.
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
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → v0.3.5--ci.1 🡡 (CodeChange)   
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → v0.0.2--ci.1 🡡 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );

        // Bob ckli pulls. Its "dev/stable" is tracking the "refs/remotes/origin/dev/stable" (because Tim has pushed in ci).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();

        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
        ❰✓❱

        """ );

        // Bob publishes a non CI version here (with its "Bob-work.txt" contribution), also from CK-PerfectEvent.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "publish" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → v0.4.0 🡡 (CodeChange)               
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → v0.1.0 🡡 (UpstreamBuild, CodeChange)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );

        // Tim ckli pulls, but before he creates the dev/stable branch. This is useless and the
        // issue below reflects this.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "checkout", "dev/stable" )).ShouldBeTrue();
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
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "checkout", "dev/stable" )).ShouldBeTrue();
        }
        else
        {
            // Using -f to allow the "dev/stable" to already exist.
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "exec", "git", "branch", "dev/stable", "-f" )).ShouldBeTrue();
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
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
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
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3+deprecated → v0.3.4 🡡 (DeprecatedVersion)               
              ║      CKt-Monitoring                v0.2.4           
              ╙      Samples/CKt-App-Sample        v0.0.0           
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+deprecated → v0.0.1 🡡 (UpstreamBuild, DeprecatedVersion)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );

        // Deprecate it now!
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "version", "deprecate", "v0.3.3", "--immediate", "--allow-update" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1           
              - →·   CKt-ActivityMonitor           v0.1.1           
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3+deprecated → v0.3.4 🡡 (DeprecatedVersion)               
              ║      CKt-Monitoring                v0.2.4           
              ╙      Samples/CKt-App-Sample        v0.0.0           
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+deprecated → v0.0.1 🡡 (UpstreamBuild, DeprecatedVersion)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );
    }

    [Test]
    public async Task fake_version_sets_the_version_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
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
            1 ╓  ⊙   CKt-PerfectEvent              v5.4.3+fake → v5.4.3 🡡 (FakeVersion)  
              ║      CKt-Monitoring                v0.2.4     
              ╙      Samples/CKt-App-Sample        v0.0.0     
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0      → v0.0.1 🡡 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories can be published.
            ❰✓❱

            """ );

    }

    [TestCase( true )]
    [TestCase( false )]
    public async Task ci_followed_by_non_ci_Async( bool useCheckout )
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = remotes.Clone( clonedFolder,
                                     ( monitor, stackPath, plugins ) => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) );
        var display = (StringScreen)context.Screen;

        var activityMonitor = context.ChangeDirectory( "CKt-ActivityMonitor" );
        var perfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        await TouchDevStableAsync( perfectEvent, useCheckout );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--ci" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  CKt-Core                      v1.0.1
              -  CKt-ActivityMonitor           v0.1.1
            1 ╓  CKt-PerfectEvent              v0.3.3 → v0.3.4--ci.1 🡡 (CodeChange)   
              ║  CKt-Monitoring                v0.2.4
              ╙  Samples/CKt-App-Sample        v0.0.0
            2 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 🡡 (UpstreamBuild)
            Required build for 2 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  CKt-Core                      v1.0.1
              -  CKt-ActivityMonitor           v0.1.1
            1 ╓  CKt-PerfectEvent              v0.3.3 → v0.3.4 🡡 (CodeChange)               
              ║  CKt-Monitoring                v0.2.4
              ╙  Samples/CKt-App-Sample        v0.0.0
            2 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1 🡡 (UpstreamBuild, CodeChange)
            Required build for 2 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );

        await TouchDevStableAsync( activityMonitor, useCheckout );
        await TouchDevStableAsync( perfectEvent, useCheckout );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "build", "-d" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.1.1
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → v0.3.5 🡡 (CodeChange)   
              ║      CKt-Monitoring                v0.2.4
              ╙      Samples/CKt-App-Sample        v0.0.0
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → v0.0.2 🡡 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories can be published.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, perfectEvent, "*build", "-d" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1
            1 - →·   CKt-ActivityMonitor           v0.1.1 → v0.1.2 🡡 (CodeChange)               
            2 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → v0.3.5 🡡 (UpstreamBuild, CodeChange)
            3 ║      CKt-Monitoring                v0.2.4 → v0.2.5 🡡 (UpstreamBuild)            
            4 ╙      Samples/CKt-App-Sample        v0.0.0 → v0.0.1 🡡 (UpstreamBuild)            
            5 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → v0.0.2 🡡 (UpstreamBuild)            
            Required build for 5 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 5 repositories can be published.
            ❰✓❱

            """ );


    }


    [Test]
    public async Task common_files_tests_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        var ckliRoot = TestHelper.SolutionFolder.RemoveLastPart().AppendPart( "CKli" );

        var commonFolder = context.CurrentStackPath.AppendPart( "Common" );
        Directory.CreateDirectory( commonFolder );

        var globalJsonContent = File.ReadAllText( ckliRoot.AppendPart( "global.json" ) );
        File.WriteAllText( commonFolder.AppendPart( "global.json" ), globalJsonContent );

        var directoryPropsContent = File.ReadAllText( ckliRoot.AppendPart( "Directory.Build.props" ) );
        File.WriteAllText( commonFolder.AppendPart( "Directory.Build.props" ), directoryPropsContent );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: stable (2 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > File 'Directory.Build.props' must be updated.
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: stable (2 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > File 'Directory.Build.props' must be updated.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: stable (2 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > File 'Directory.Build.props' must be updated.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (2 content issues)
            │ │ > File 'global.json' must be created.
            │ │ > File 'Directory.Build.props' must be updated.
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (2 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.props
            │ │ - global.json
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: stable (2 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.props
            │ │ - global.json
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

        // Test with different case and space.
        File.Delete( initOnlyFilePath );
        initOnlyFilePath = commonFolder.AppendPart( " [ iNItonlY ]Justfortest.txt" );
        File.WriteAllText( initOnlyFilePath, "Hello" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "checkout", "dev/stable" )).ShouldBeTrue();

        Directory.CreateDirectory( inAppSample.CurrentDirectory.Combine( "CKt.SomeApp/Doc" ) );
        File.WriteAllText( inAppSample.CurrentDirectory.Combine( "CKt.SomeApp/Doc/Package.md" ), """
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
            1 ╙  ⊙   Samples/CKt-App-Sample        v0.0.0 → v0.0.1 🡡 (CodeChange)
              -      Samples/CKt-Sample-Monitoring v0.0.0
            Required build for 1 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 1 repositories can be published.
            ❰✓❱
            
            """ );

    }


    [TestCase( true )]
    [TestCase( false )]
    public async Task with_ci_0_Async( bool useCheckout )
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = remotes.Clone( clonedFolder,
                                     ( monitor, stackPath, plugins ) => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) );
        var display = (StringScreen)context.Screen;

        var activityMonitor = context.ChangeDirectory( "CKt-ActivityMonitor" );
        var perfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        await TouchDevStableAsync( perfectEvent, useCheckout );

        // The ci.0 is generated for all unchanged repos.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--ci.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  CKt-Core                      v1.0.1 → v1.0.2--ci.0
              -  CKt-ActivityMonitor           v0.1.1 → v0.1.2--ci.0
            1 ╓  CKt-PerfectEvent              v0.3.3 → v0.3.4--ci.1 🡡 (CodeChange)   
              ║  CKt-Monitoring                v0.2.4 → v0.2.5--ci.0
              ╙  Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.0
            2 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 🡡 (UpstreamBuild)
            Required build for 2 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );

        // Once published, they don't change the regular version.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
              -  CKt-Core                      v1.0.1
              -  CKt-ActivityMonitor           v0.1.1
            1 ╓  CKt-PerfectEvent              v0.3.3 → v0.3.4 🡡 (CodeChange)               
              ║  CKt-Monitoring                v0.2.4
              ╙  Samples/CKt-App-Sample        v0.0.0
            2 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1 🡡 (UpstreamBuild, CodeChange)
            Required build for 2 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories must be published.
            ❰✓❱

            """ );

    }


}
