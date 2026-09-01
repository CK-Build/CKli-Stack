using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests.Integration;

[TestFixture]
public partial class S3ᅳSamplePublishedᅳTests
{
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
            Required build for 2 from the single pivot out of 6 repositories and 2 can be published.
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
            Required build for 2 from the single pivot out of 6 repositories and 2 can be published.
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
        var directoryTargetsContent = File.ReadAllText( ckliRoot.AppendPart( "Directory.Build.targets" ) );
        File.WriteAllText( commonFolder.AppendPart( "Directory.Build.targets" ), directoryTargetsContent );

        // The "real" .slnx file has no impact here as all the .slnx exist already.
        File.WriteAllText( commonFolder.AppendPart( "[InitOnly]$RepositoryName$.slnx" ), "<Solution></Solution>" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (1)
            │ > Content issues.
            │ │ Branch: stable (4 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.targets
            │ │ - global.json
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: stable (4 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.targets
            │ │ - global.json
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: stable (4 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.targets
            │ │ - global.json
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (4 content issues)
            │ │ > 2 files must be created:
            │ │ - Directory.Build.targets
            │ │ - global.json
            │ │ > 2 files must be updated:
            │ │ - nuget.config
            │ │ - Directory.Build.props
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: stable (4 content issues)
            │ │ > 3 files must be created:
            │ │ - Directory.Build.props
            │ │ - Directory.Build.targets
            │ │ - global.json
            │ │ > File 'nuget.config' must be updated.
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: stable (4 content issues)
            │ │ > 3 files must be created:
            │ │ - Directory.Build.props
            │ │ - Directory.Build.targets
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
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
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
            │ │ > Branch: dev/stable (1 content issue)
            │ │ │ > File must be moved: JustForTest.txt → Justfortest.txt (case differ)
            > CKt-ActivityMonitor (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > CKt-PerfectEvent (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > CKt-Monitoring (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > Samples/CKt-Sample-Monitoring (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            > Samples/CKt-App-Sample (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
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
            Required build for 1 from the single pivot out of 6 repositories and 1 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱
            
            """ );

    }


}
