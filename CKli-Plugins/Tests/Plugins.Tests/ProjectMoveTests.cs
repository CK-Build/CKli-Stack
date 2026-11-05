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
