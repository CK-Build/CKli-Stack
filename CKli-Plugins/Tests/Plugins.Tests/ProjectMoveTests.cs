using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The "project move" command: inside a repository and across repositories. These tests use the fake build
/// harness (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>) to arrange the
/// repositories: the command itself never builds.
/// </summary>
public class ProjectMoveTests
{
    /// <summary>
    /// Inside a repository, the moved project goes to the destination folder and every reference follows: the
    /// project's own references, and those of the projects that reference it. Its solution folder is derived from
    /// its new path.
    /// </summary>
    [Test]
    public async Task move_inside_a_repository_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var repo = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( repo ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "move", "Lib.A", "-t", "Libs" )).ShouldBeTrue();

        var root = repo.WorkingFolderPath;
        File.Exists( root.Combine( "Libs/Lib.A/Lib.A.csproj" ) ).ShouldBeTrue();
        Directory.Exists( root.Combine( "Lib.A" ) ).ShouldBeFalse();
        // The moved project's own reference: one level deeper.
        ReadProjectReferences( root.Combine( "Libs/Lib.A/Lib.A.csproj" ) ).ShouldBe( @"..\..\Lib.C\Lib.C.csproj" );
        // The references to the moved project.
        ReadProjectReferences( root.Combine( "Lib.B/Lib.B.csproj" ) ).ShouldBe( @"..\Libs\Lib.A\Lib.A.csproj" );
        ReadProjectReferences( root.Combine( "Tests/Lib.A.Tests/Lib.A.Tests.csproj" ) ).ShouldBe( @"..\..\Libs\Lib.A\Lib.A.csproj" );
        // The solution: the project is in the "/Libs/" solution folder.
        var slnx = File.ReadAllText( root.AppendPart( repo.SolutionFileName ) );
        slnx.ShouldContain( """<Folder Name="/Libs/">""" );
        slnx.ShouldContain( """<Project Path="Libs/Lib.A/Lib.A.csproj" />""" );
        slnx.ShouldNotContain( """Path="Lib.A/Lib.A.csproj""" );
    }

    /// <summary>
    /// Across repositories, the projects keep their paths when the move is run from the target's root (their common
    /// folder is the repository root), the references between them stay project references, those that cross the
    /// repository boundary become package references, and the target's starting version is above the source's
    /// versions.
    /// </summary>
    [Test]
    public async Task move_across_repositories_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", null ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C,Lib.A,Lib.A.Tests" )).ShouldBeTrue();

        var s = source.WorkingFolderPath;
        var t = target.WorkingFolderPath;
        File.Exists( t.Combine( "Lib.A/Lib.A.csproj" ) ).ShouldBeTrue();
        File.Exists( t.Combine( "Lib.C/Lib.C.csproj" ) ).ShouldBeTrue();
        File.Exists( t.Combine( "Tests/Lib.A.Tests/Lib.A.Tests.csproj" ) ).ShouldBeTrue();
        Directory.Exists( s.Combine( "Lib.A" ) ).ShouldBeFalse();
        Directory.Exists( s.Combine( "Tests" ) ).ShouldBeFalse();
        // Between moved projects: unchanged project references.
        ReadProjectReferences( t.Combine( "Lib.A/Lib.A.csproj" ) ).ShouldBe( @"..\Lib.C\Lib.C.csproj" );
        ReadProjectReferences( t.Combine( "Tests/Lib.A.Tests/Lib.A.Tests.csproj" ) ).ShouldBe( @"..\..\Lib.A\Lib.A.csproj" );
        // From the source to a moved project: a package reference in the target's starting version.
        ReadProjectReferences( s.Combine( "Lib.B/Lib.B.csproj" ) ).ShouldBeEmpty();
        File.ReadAllText( s.Combine( "Lib.B/Lib.B.csproj" ) ).ShouldContain( """<PackageReference Include="Lib.A" Version="1.0.1" />""" );
        // The target starts above the source's v1.0.0.
        using( var e = target.CreateEditor() )
        {
            e.GitRepository.Repository.Tags.Select( t => t.FriendlyName ).ShouldContain( "v1.0.1+fake" );
        }
        // The solutions.
        var sourceSlnx = File.ReadAllText( s.AppendPart( source.SolutionFileName ) );
        sourceSlnx.ShouldNotContain( "Lib.A" );
        sourceSlnx.ShouldNotContain( "/Tests/" );
        var targetSlnx = File.ReadAllText( t.AppendPart( target.SolutionFileName ) );
        targetSlnx.ShouldContain( """<Project Path="Lib.A/Lib.A.csproj" />""" );
        targetSlnx.ShouldContain( """<Folder Name="/Tests/">""" );
        targetSlnx.ShouldContain( """<Project Path="Tests/Lib.A.Tests/Lib.A.Tests.csproj" />""" );
    }

    /// <summary>
    /// A move lands on the "dev/" branches. Here the target's "dev/sierra" has no commit of its own (just opened): it
    /// is deleted as useless when the branch model is read, which checks out "sierra". The move recreates and checks
    /// out "dev/sierra" before moving anything, so the moved projects are committed on it, not on "sierra".
    /// </summary>
    [Test]
    public async Task a_move_lands_on_the_dev_branch_even_when_it_was_useless_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", null ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        // The source's "dev/sierra" has a commit of its own, the target's has none.
        TestHelper.TouchAndCommit( source.WorkingFolderPath, branchName: "dev/sierra" );
        CurrentBranch( target ).ShouldBe( "dev/sierra" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C,Lib.A", "--with-tests" )).ShouldBeTrue();

        CurrentBranch( target ).ShouldBe( "dev/sierra" );
        CurrentBranch( source ).ShouldBe( "dev/sierra" );
        File.Exists( target.WorkingFolderPath.Combine( "Lib.A/Lib.A.csproj" ) ).ShouldBeTrue();

        static string CurrentBranch( FakeBuildRepo repo )
        {
            using var e = repo.CreateEditor();
            return e.GitRepository.CurrentBranchName;
        }
    }

    /// <summary>
    /// "--with-tests" moves the "X.Tests" project of each moved project "X" (here "Lib.A.Tests" for "Lib.A"; "Lib.C"
    /// has none): it lands in "Tests/" as if it had been named.
    /// </summary>
    [Test]
    public async Task with_tests_moves_the_test_projects_of_the_moved_projects_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", null ).ConfigureAwait( false );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C,Lib.A", "--with-tests" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Test projects added by --with-tests: Lib.A.Tests." );

        var t = target.WorkingFolderPath;
        File.Exists( t.Combine( "Tests/Lib.A.Tests/Lib.A.Tests.csproj" ) ).ShouldBeTrue();
        ReadProjectReferences( t.Combine( "Tests/Lib.A.Tests/Lib.A.Tests.csproj" ) ).ShouldBe( @"..\..\Lib.A\Lib.A.csproj" );
        Directory.Exists( source.WorkingFolderPath.Combine( "Tests" ) ).ShouldBeFalse();
        File.ReadAllText( t.AppendPart( target.SolutionFileName ) ).ShouldContain( """<Project Path="Tests/Lib.A.Tests/Lib.A.Tests.csproj" />""" );
    }

    /// <summary>
    /// Only the tracked files move: an ignored file of a moved folder (a build output here) stays where it is, and
    /// the plan says so.
    /// </summary>
    [Test]
    public async Task only_the_tracked_files_move_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        File.AppendAllText( source.WorkingFolderPath.AppendPart( ".gitignore" ), Environment.NewLine + "bin/" + Environment.NewLine );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var ignored = source.WorkingFolderPath.Combine( "Lib.A/bin/Debug/Lib.A.dll" );
        Directory.CreateDirectory( ignored.RemoveLastPart() );
        File.WriteAllText( ignored, "Not a real assembly." );
        var target = await world.CreateRepoAsync( "X-Target", null ).ConfigureAwait( false );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C,Lib.A", "--with-tests" )).ShouldBeTrue();
        display.ToString().ShouldContain( "1 ignored files stay in 'X-Source/Lib.A'." );

        File.Exists( target.WorkingFolderPath.Combine( "Lib.A/Lib.A.csproj" ) ).ShouldBeTrue();
        Directory.Exists( target.WorkingFolderPath.Combine( "Lib.A/bin" ) ).ShouldBeFalse();
        File.Exists( ignored ).ShouldBeTrue();
        File.Exists( source.WorkingFolderPath.Combine( "Lib.A/Lib.A.csproj" ) ).ShouldBeFalse();
    }

    /// <summary>
    /// A dry run displays the plan (the moved folders, the starting version, the reference changes) and changes
    /// nothing: no folder moves, no "+fake" tag.
    /// </summary>
    [Test]
    public async Task a_dry_run_displays_the_plan_and_changes_nothing_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", null ).ConfigureAwait( false );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C,Lib.A,Lib.A.Tests", "--dry-run" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "Dry run: nothing is changed." );
        text.ShouldContain( "X-Source/Tests/Lib.A.Tests -> X-Target/Tests/Lib.A.Tests (solution folder '/Tests/')." );
        text.ShouldContain( "Starting version of 'X-Target': 'v1.0.1+fake'." );
        text.ShouldContain( @"Lib.B: project reference '..\Lib.A\Lib.A.csproj' -> package reference 'Lib.A' 1.0.1." );

        File.Exists( source.WorkingFolderPath.Combine( "Lib.A/Lib.A.csproj" ) ).ShouldBeTrue();
        Directory.Exists( target.WorkingFolderPath.Combine( "Lib.A" ) ).ShouldBeFalse();
        using( var e = target.CreateEditor() )
        {
            e.GitRepository.Repository.Tags.Select( t => t.FriendlyName ).ShouldNotContain( "v1.0.1+fake" );
        }
    }

    /// <summary>
    /// A name that matches projects of several repositories is refused with their paths, a folder prefix chooses one.
    /// </summary>
    [Test]
    public async Task an_ambiguous_name_is_refused_and_a_folder_prefix_chooses_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r1 = await world.CreateRepoAsync( "X-One", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( r1 ).ConfigureAwait( false );
        var r2 = await world.CreateRepoAsync( "X-Two", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( r2 ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r1.Root, "project", "move", "Lib.A.Tests", "-t", "Moved/Tests" )).ShouldBeFalse();
            logs.ShouldContain( l => l.StartsWith( "'Lib.A.Tests' matches 2 projects" ) );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r1.Root, "project", "move", "X-One/Tests/Lib.A.Tests", "-t", "Moved/Tests" )).ShouldBeTrue();
        File.Exists( r1.WorkingFolderPath.Combine( "Moved/Tests/Lib.A.Tests/Lib.A.Tests.csproj" ) ).ShouldBeTrue();
    }

    /// <summary>
    /// A move that makes the target and a source depend on each other is refused, and nothing is changed.
    /// </summary>
    [Test]
    public async Task a_move_that_creates_a_cycle_is_refused_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", null ).ConfigureAwait( false );

        // Lib.A references Lib.C (that stays) and Lib.B (that stays) references Lib.A.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.A,Lib.A.Tests" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "would depend on each other" ) );
        }
        File.Exists( source.WorkingFolderPath.Combine( "Lib.A/Lib.A.csproj" ) ).ShouldBeTrue();
        Directory.Exists( target.WorkingFolderPath.Combine( "Lib.A" ) ).ShouldBeFalse();
    }

    // Adds to a repository: Lib.C, Lib.A that references Lib.C, Lib.B that references Lib.A, and
    // Tests/Lib.A.Tests that references Lib.A. Then commits.
    static async Task AddLibrariesAsync( FakeBuildRepo repo )
    {
        var root = repo.WorkingFolderPath;
        WriteProject( root, "Lib.C", null );
        WriteProject( root, "Lib.A", @"..\Lib.C\Lib.C.csproj" );
        WriteProject( root, "Lib.B", @"..\Lib.A\Lib.A.csproj" );
        WriteProject( root, "Tests/Lib.A.Tests", @"..\..\Lib.A\Lib.A.csproj" );
        var slnx = root.AppendPart( repo.SolutionFileName );
        File.WriteAllText( slnx, File.ReadAllText( slnx ).Replace( "</Solution>", """
                <Folder Name="/Tests/">
                    <Project Path="Tests/Lib.A.Tests/Lib.A.Tests.csproj" />
                </Folder>
                <Project Path="Lib.A/Lib.A.csproj" />
                <Project Path="Lib.B/Lib.B.csproj" />
                <Project Path="Lib.C/Lib.C.csproj" />
            </Solution>
            """ ) );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "commit", "Added libraries." )).ShouldBeTrue();

        static void WriteProject( NormalizedPath root, NormalizedPath folder, string? projectReference )
        {
            var name = folder.LastPart;
            Directory.CreateDirectory( root.Combine( folder ) );
            var reference = projectReference == null ? "" : $"""<ProjectReference Include="{projectReference}" />""";
            File.WriteAllText( root.Combine( folder ).AppendPart( name + ".csproj" ), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                    <ItemGroup>
                        {reference}
                    </ItemGroup>
                </Project>
                """ );
        }
    }

    static string ReadProjectReferences( NormalizedPath csproj )
    {
        var d = System.Xml.Linq.XDocument.Load( csproj );
        return d.Descendants( "ProjectReference" ).Select( e => (string?)e.Attribute( "Include" ) ).Concatenate();
    }
}
