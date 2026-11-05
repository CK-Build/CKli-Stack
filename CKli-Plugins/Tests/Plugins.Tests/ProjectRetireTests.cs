using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The "project retire" command: turns packable projects into tombstones. The repositories are arranged with the
/// fake build harness (the command itself never builds): Lib.C &lt;- Lib.A &lt;- Lib.B, and Tests/Lib.A.Tests -&gt; Lib.A.
/// </summary>
public class ProjectRetireTests
{
    const string _guideUrl = "https://github.com/Some-Org/Some-Repo/blob/stable/New.Lib/README.md";

    /// <summary>
    /// A leaf package is retired with a reason: its code is deleted, its project becomes the tombstone with its
    /// MSBuild ".targets" and its documentation, and this lands on the "dev/" branch, uncommitted.
    /// </summary>
    [Test]
    public async Task retire_a_package_with_a_reason_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var repo = await stack.DefaultWorld.CreateRepoAsync( "X-Libs", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( repo ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B", "--reason", "Use Lib.A directly." )).ShouldBeTrue();

        var folder = repo.WorkingFolderPath.AppendPart( "Lib.B" );
        File.Exists( folder.AppendPart( "Code.cs" ) ).ShouldBeFalse( "The code is deleted." );
        var csproj = File.ReadAllText( folder.AppendPart( "Lib.B.csproj" ) );
        csproj.ShouldContain( "<TargetFramework>net10.0</TargetFramework>" );
        csproj.ShouldContain( "<IncludeBuildOutput>false</IncludeBuildOutput>" );
        csproj.ShouldContain( "<Description>Deprecated: Use Lib.A directly.</Description>" );
        csproj.ShouldContain( @"<PackagePath>buildTransitive\Lib.B.targets</PackagePath>" );
        csproj.ShouldNotContain( "ProjectReference", customMessage: "A tombstone has no dependencies." );
        var targets = File.ReadAllText( folder.Combine( "MSBuild/Lib.B.targets" ) );
        targets.ShouldContain( """<Target Name="Lib_B_IsDeprecated" BeforeTargets="ResolveAssemblyReferences">""" );
        targets.ShouldContain( """Text="Package 'Lib.B' is deprecated and has no replacement: Use Lib.A directly." """ );
        File.ReadAllText( folder.AppendPart( "README.md" ) ).ShouldStartWith( "# Lib.B\r\n\r\n**Deprecated: no replacement.**" );
        File.ReadAllText( folder.Combine( "docs/Package.md" ) ).ShouldContain( "Use Lib.A directly." );
        // Lands on the "dev/" branch, nothing committed.
        using var e = repo.CreateEditor();
        e.GitRepository.CurrentBranchName.ShouldBe( "dev/stable" );
        e.GitRepository.GetSimpleStatusInfo().IsDirty.ShouldBeTrue();
    }

    /// <summary>
    /// A package that the World doesn't produce is given by the URL of its README: the package is named after its folder.
    /// </summary>
    [Test]
    public async Task retire_a_package_replaced_by_an_external_one_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var repo = await stack.DefaultWorld.CreateRepoAsync( "X-Libs", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( repo ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B", "--replacement", _guideUrl )).ShouldBeTrue();

        var folder = repo.WorkingFolderPath.AppendPart( "Lib.B" );
        File.ReadAllText( folder.Combine( "MSBuild/Lib.B.targets" ) )
            .ShouldContain( $"""Text="Package 'Lib.B' is deprecated and must be replaced by 'New.Lib': see {_guideUrl}." """ );
        File.ReadAllText( folder.AppendPart( "Lib.B.csproj" ) ).ShouldContain( "<Description>Deprecated: replaced by New.Lib.</Description>" );
        File.ReadAllText( folder.Combine( "docs/Package.md" ) )
            .ShouldStartWith( $"**Deprecated: replaced by `New.Lib`** ([documentation]({_guideUrl}))." );
    }

    /// <summary>
    /// A package still used in the World can't be retired: the consumers are listed (a test project included, with
    /// the "--with-tests" hint) and nothing changes.
    /// </summary>
    [Test]
    public async Task a_used_package_cannot_be_retired_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var repo = await stack.DefaultWorld.CreateRepoAsync( "X-Libs", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( repo ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.A", "--reason", "Obsolete." )).ShouldBeFalse();
            var error = logs.ShouldContain( t => t.StartsWith( "The retired packages must not be used any more in the World:" ) );
            logs.ShouldContain( t => t.Contains( "'X-Libs/Lib.B/Lib.B.csproj' references the project 'Lib.A'." )
                                     && t.Contains( "'X-Libs/Tests/Lib.A.Tests/Lib.A.Tests.csproj' references the project 'Lib.A'." )
                                     && t.Contains( "--with-tests" ) );
        }
        using var e = repo.CreateEditor();
        e.GitRepository.GetSimpleStatusInfo().IsDirty.ShouldBeFalse();
    }

    /// <summary>
    /// Once its consumer Lib.B is a tombstone (that has no dependencies), Lib.A can be retired: "--with-tests" deletes
    /// its test project and removes it from the solution.
    /// </summary>
    [Test]
    public async Task with_tests_deletes_the_test_projects_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var repo = await stack.DefaultWorld.CreateRepoAsync( "X-Libs", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( repo ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B", "--reason", "Obsolete." )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "commit", "Lib.B is retired." )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.A", "--replacement", _guideUrl, "--with-tests" )).ShouldBeTrue();

        var root = repo.WorkingFolderPath;
        File.Exists( root.Combine( "Lib.A/MSBuild/Lib.A.targets" ) ).ShouldBeTrue();
        Directory.Exists( root.Combine( "Tests" ) ).ShouldBeFalse();
        File.ReadAllText( root.AppendPart( repo.SolutionFileName ) ).ShouldNotContain( "Lib.A.Tests" );
        // Retired again: it is already a tombstone.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "commit", "Lib.A is retired." )).ShouldBeTrue();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.A", "--reason", "Obsolete." )).ShouldBeFalse();
            logs.ShouldContain( "Project 'X-Libs/Lib.A/Lib.A.csproj' is already a tombstone." );
        }
    }

    /// <summary>
    /// The replacement produced by the World gives its documentation URL from its repository: an origin that is not
    /// an http(s) one (the file system remotes of the tests) can't, and a dry run changes nothing.
    /// </summary>
    [Test]
    public async Task checks_and_dry_run_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var repo = await stack.DefaultWorld.CreateRepoAsync( "X-Libs", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( repo ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B" )).ShouldBeFalse();
            logs.ShouldContain( "A tombstone says what to do: --replacement or --reason (or both) must be specified." );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B", "--replacement", "Lib.C" )).ShouldBeFalse();
            logs.ShouldContain( t => t.StartsWith( "The documentation of the replacement can't be derived from the origin 'file://" ) );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B", "--replacement", "Unknown.Lib" )).ShouldBeFalse();
            logs.ShouldContain( "No project of the World produces 'Unknown.Lib': --replacement must be the URL of its README ('.../Unknown.Lib/README.md')." );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B", "--replacement", "https://example.com/doc.html" )).ShouldBeFalse();
            logs.ShouldContain( "The --replacement URL 'https://example.com/doc.html' must be the README of the replacement: '.../<PackageId>/README.md'." );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.A.Tests", "--reason", "Obsolete." )).ShouldBeFalse();
            logs.ShouldContain( "Project 'X-Libs/Tests/Lib.A.Tests/Lib.A.Tests.csproj' produces no package: there is nothing to retire, delete it." );
        }
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "project", "retire", "Lib.B", "--reason", "Obsolete.", "--dry-run" )).ShouldBeTrue();
        var screen = stack.Screen.ToString();
        screen.ShouldContain( "Dry run: nothing is changed." );
        screen.ShouldContain( "Package 'Lib.B' is deprecated and has no replacement: Obsolete." );
        using var e = repo.CreateEditor();
        e.GitRepository.GetSimpleStatusInfo().IsDirty.ShouldBeFalse();
    }

    static async Task AddLibrariesAsync( FakeBuildRepo repo )
    {
        var root = repo.WorkingFolderPath;
        WriteProject( root, "Lib.C", null );
        WriteProject( root, "Lib.A", @"..\Lib.C\Lib.C.csproj" );
        WriteProject( root, "Lib.B", @"..\Lib.A\Lib.A.csproj" );
        WriteProject( root, "Tests/Lib.A.Tests", @"..\..\Lib.A\Lib.A.csproj" );
        File.WriteAllText( root.Combine( "Lib.B/Code.cs" ), "namespace Lib.B; public class C {}" );
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
}
