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

        // The Common files are missing from every repository: fix them all. The propagation mechanism
        // itself (creation, update, "[InitOnly]" files and case differing names) is covered by
        // CommonFilesTests on the fake build harness. Only the real build below needs these actual
        // global.json and Directory.Build.props/targets, so only that is asserted here.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();

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
