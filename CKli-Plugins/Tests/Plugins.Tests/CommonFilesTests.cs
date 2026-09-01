using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The stack's "Common" folder: files declared there are propagated to every repository of the World.
/// Uses the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class CommonFilesTests
{
    [Test]
    public async Task common_files_are_propagated_to_every_repository_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rApp = await world.CreateRepoAsync( "X-App-Sample", "v0.0.0", "Samples", rCore ).ConfigureAwait( false );

        var commonFolder = world.WorldRoot.CurrentStackPath.AppendPart( "Common" );
        Directory.CreateDirectory( commonFolder );
        File.WriteAllText( commonFolder.AppendPart( "Directory.Build.props" ), """
            <Project>
              <PropertyGroup>
                <LangVersion>preview</LangVersion>
              </PropertyGroup>
            </Project>

            """ );

        // The file is missing from both repositories.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > X-Core (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'Directory.Build.props' must be created.
            > Samples/X-App-Sample (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'Directory.Build.props' must be created.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // An "[InitOnly]" file is created if missing but never updated afterwards: the prefix is not part of
        // the propagated file name.
        var initOnlyFilePath = commonFolder.AppendPart( "[InitOnly] JustForTest.txt" );
        File.WriteAllText( initOnlyFilePath, "Hello" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > X-Core (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            > Samples/X-App-Sample (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'JustForTest.txt' must be created.
            ❰✓❱

            """ );

        // Fix X-Core only: the other repository keeps its issue.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "issue", "--fix" )).ShouldBeTrue();

        // The prefix is case insensitive and the spaces around it are ignored, but the file name itself is
        // case sensitive: X-Core, which has the file, must MOVE it while the other must create it.
        File.Delete( initOnlyFilePath );
        File.WriteAllText( commonFolder.AppendPart( " [ iNItonlY ]Justfortest.txt" ), "Hello" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > X-Core (1)
            │ > Content issues.
            │ │ > Branch: dev/stable (1 content issue)
            │ │ │ > File must be moved: JustForTest.txt → Justfortest.txt (case differ)
            > Samples/X-App-Sample (1)
            │ > Content issues.
            │ │ Branch: dev/stable (1 content issue)
            │ │ > File 'Justfortest.txt' must be created.
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );
    }
}
