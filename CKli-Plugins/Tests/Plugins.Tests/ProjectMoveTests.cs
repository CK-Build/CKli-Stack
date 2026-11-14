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

    /// <summary>
    /// Out of the root branch, the target's starting version is a branch fake: it is set on the target's "dev/sierra"
    /// (on an empty commit of its own, the branch having no commit), the root branch doesn't contain it and it is
    /// registered to be pushed by the next push. The "sierra" builds are based on it, the root branch's versions are
    /// unchanged. Closing "sierra" integrates it into "dev/stable": it becomes the root's starting version.
    /// </summary>
    [Test]
    public async Task a_move_on_a_branch_sets_a_branch_fake_that_reaches_the_root_with_the_integration_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", "v2.0.0" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( source.WorkingFolderPath, branchName: "dev/sierra" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C,Lib.A", "--with-tests" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Starting version of 'X-Target' on 'dev/sierra': 'v2.0.1+fake' (it reaches 'stable' with the integration of 'sierra')." );

        using( var e = target.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            var fakeCommit = (LibGit2Sharp.Commit)git.Tags["v2.0.1+fake"].ShouldNotBeNull().PeeledTarget;
            fakeCommit.MessageShort.ShouldBe( "Empty commit carrying the 'v2.0.1+fake' version tag." );
            git.Branches["dev/sierra"].Tip.Sha.ShouldBe( fakeCommit.Sha );
            foreach( var root in new[] { "stable", "dev/stable" } )
            {
                var tip = git.Branches[root]?.Tip;
                if( tip != null ) git.ObjectDatabase.FindMergeBase( fakeCommit, tip )?.Sha.ShouldNotBe( fakeCommit.Sha );
            }
            e.GitRepository.PersistentDeferredPushRefSpecs.ShouldBe( ["+refs/tags/v2.0.1+fake"] );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, source.Root, "commit", "Moved Lib.A and Lib.C to X-Target." )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "commit", "Received Lib.A and Lib.C from X-Source." )).ShouldBeTrue();

        // On "sierra": X-Target is based on its branch fake.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Target v2.0.1+fake → ⏚/v2.0.1-sierra.0.ci.1 (FakeVersion, CodeChange)  
            2 -  X-Source v1.0.0      → ⏚/v1.0.1-sierra.0.ci.4 (UpstreamBuild, CodeChange)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // On "stable": the branch fake doesn't exist, X-Target is still based on v2.0.0.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 ╓  X-Source v1.0.0 → ⏚/v1.0.1--ci.1 (CodeChange)
              ╙  X-Target v2.0.0
            Required build for 1 repositories across the 2 repositories and 1 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Closing "sierra" integrates the fake into "dev/stable": it is X-Target's starting version on "stable".
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "close", "sierra" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Target v2.0.1+fake → ⏚/v2.0.1--ci.1 (FakeVersion, CodeChange)  
            2 -  X-Source v1.0.0      → ⏚/v1.0.1--ci.4 (UpstreamBuild, CodeChange)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }

    /// <summary>
    /// Two branches that don't contain each other have their own branch fakes, and two branches can't start the same
    /// version: the move on "sierra" starts above the "romeo" one. ("romeo" is opened first: "sierra" is its parent in
    /// the prerelease chain, opened later it starts from "stable" and doesn't contain "romeo".) Each branch is based
    /// on its own fake.
    /// </summary>
    [Test]
    public async Task two_branches_have_their_own_branch_fakes_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", "v2.0.0" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, source.Root, "commit", "Moved Lib.C." )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "commit", "Received Lib.C." )).ShouldBeTrue();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.B" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, source.Root, "commit", "Moved Lib.B." )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "commit", "Received Lib.B." )).ShouldBeTrue();

        using( var e = target.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            var romeoFake = (LibGit2Sharp.Commit)git.Tags["v2.0.1+fake"].ShouldNotBeNull().PeeledTarget;
            var sierraFake = (LibGit2Sharp.Commit)git.Tags["v2.0.2+fake"].ShouldNotBeNull().PeeledTarget;
            git.ObjectDatabase.FindMergeBase( romeoFake, git.Branches["dev/sierra"].Tip )?.Sha.ShouldNotBe( romeoFake.Sha );
            git.ObjectDatabase.FindMergeBase( sierraFake, git.Branches["dev/romeo"].Tip )?.Sha.ShouldNotBe( sierraFake.Sha );
            e.GitRepository.PersistentDeferredPushRefSpecs.Order().ShouldBe( ["+refs/tags/v2.0.1+fake", "+refs/tags/v2.0.2+fake"] );
        }

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Source v1.0.0      → ⏚/v1.0.1-sierra.0.ci.2 (CodeChange)                            
            2 -  X-Target v2.0.2+fake → ⏚/v2.0.2-sierra.0.ci.2 (UpstreamBuild, FakeVersion, CodeChange)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "romeo" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Target v2.0.1+fake → ⏚/v2.0.1-romeo.0.ci.1 (FakeVersion, CodeChange)  
            2 -  X-Source v1.0.0      → ⏚/v1.0.1-romeo.0.ci.3 (UpstreamBuild, CodeChange)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );
    }

    /// <summary>
    /// A branch fake is abandoned when its branch is gone: removed from the branch model ("branch close --discard") AND
    /// its git branches deleted. While the git branches exist, it is pending (another clone may still work on it):
    /// nothing is reported. Once abandoned, "ckli issue --fix" removes it with its local versions, and forgets its
    /// deferred push (it has never been pushed): the next push succeeds.
    /// </summary>
    [Test]
    public async Task an_abandoned_branch_fake_is_removed_with_its_local_versions_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;
        var (source, target) = await ArrangeMoveOnSierraAsync( world ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        ReadTags( target ).ShouldBe( "local/v2.0.1-sierra.0.ci.1, v2.0.0, v2.0.1+fake" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "close", "sierra", "--discard" )).ShouldBeTrue();
        // The git branches still exist: the fake is pending.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "abandoned" );

        DeleteBranches( source, "sierra", "dev/sierra" );
        DeleteBranches( target, "sierra", "dev/sierra" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Found 1 abandoned branch fake versions." );
        display.ToString().ShouldContain( "- 'v2.0.1+fake' of branch 'sierra' (local versions: local/v2.0.1-sierra.0.ci.1)." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();
        ReadTags( target ).ShouldBe( "v2.0.0" );
        using( var e = target.CreateEditor() )
        {
            e.GitRepository.PersistentDeferredPushRefSpecs.ShouldBeEmpty();
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "push" )).ShouldBeTrue();
    }

    /// <summary>
    /// The next push sends the branch fake to the remote, with its branches. Abandoning it means deleting its branches
    /// from the remote too: while "origin/sierra" exists, it is pending. Once abandoned, it is deleted from the remote
    /// (a "+invalid" can't cancel a "+fake").
    /// </summary>
    [Test]
    public async Task a_pushed_abandoned_branch_fake_is_deleted_from_the_remote_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var (source, target) = await ArrangeMoveOnSierraAsync( world ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "push" )).ShouldBeTrue();
        RemoteTags( stack, "X-Target" ).ShouldContain( "v2.0.1+fake" );
        using( var e = target.CreateEditor() )
        {
            e.GitRepository.PersistentDeferredPushRefSpecs.ShouldBeEmpty();
        }

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "close", "sierra", "--discard" )).ShouldBeTrue();
        DeleteBranches( source, "sierra", "dev/sierra" );
        DeleteBranches( target, "sierra", "dev/sierra" );
        // The remote branches still exist: pending.
        var display = stack.Screen;
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "abandoned" );

        DeleteRemoteBranches( stack, source, "sierra", "dev/sierra" );
        DeleteRemoteBranches( stack, target, "sierra", "dev/sierra" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();

        ReadTags( target ).ShouldBe( "v2.0.0" );
        RemoteTags( stack, "X-Target" ).ShouldNotContain( "v2.0.1+fake" );
        RemoteTags( stack, "X-Target" ).ShouldNotContain( t => t.Contains( "invalid" ) );
    }

    /// <summary>
    /// An abandoned branch fake on which a published version is based is kept: it is what makes this version
    /// understandable. It is ignored (no branch contains it) and not reported as an issue.
    /// </summary>
    [Test]
    public async Task an_abandoned_branch_fake_with_a_published_version_is_kept_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;
        var (source, target) = await ArrangeMoveOnSierraAsync( world ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        ReadTags( target ).ShouldBe( "v2.0.0, v2.0.1+fake, v2.0.1-sierra.0.ci.1" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "close", "sierra", "--discard" )).ShouldBeTrue();
        DeleteBranches( source, "sierra", "dev/sierra" );
        DeleteBranches( target, "sierra", "dev/sierra" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "abandoned" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();
        ReadTags( target ).ShouldBe( "v2.0.0, v2.0.1+fake, v2.0.1-sierra.0.ci.1" );
    }

    // X-Source (v1.0.0, with the libraries) and X-Target (v2.0.0). On "sierra", Lib.C and Lib.A move to X-Target (its
    // branch fake is 'v2.0.1+fake'), and the move is committed.
    static async Task<(FakeBuildRepo Source, FakeBuildRepo Target)> ArrangeMoveOnSierraAsync( FakeBuildWorld world )
    {
        var source = await world.CreateRepoAsync( "X-Source", "v1.0.0" ).ConfigureAwait( false );
        await AddLibrariesAsync( source ).ConfigureAwait( false );
        var target = await world.CreateRepoAsync( "X-Target", "v2.0.0" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "project", "move", "Lib.C,Lib.A", "--with-tests" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, source.Root, "commit", "Moved Lib.A and Lib.C to X-Target." )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target.Root, "commit", "Received Lib.A and Lib.C from X-Source." )).ShouldBeTrue();
        return (source, target);
    }

    static void DeleteBranches( FakeBuildRepo repo, params string[] names )
    {
        using var e = repo.CreateEditor();
        foreach( var n in names )
        {
            if( e.GitRepository.Repository.Branches[n] != null ) e.GitRepository.Repository.Branches.Remove( n );
        }
    }

    // Deletes the branches from the remote and their remote-tracking branches (what "git push origin --delete" does).
    static void DeleteRemoteBranches( FakeBuildStack stack, FakeBuildRepo repo, params string[] names )
    {
        using( var remote = new LibGit2Sharp.Repository( stack.Remotes.GetUriFor( repo.RepositoryName ).LocalPath ) )
        {
            foreach( var n in names )
            {
                if( remote.Branches[n] != null ) remote.Branches.Remove( n );
            }
        }
        using var e = repo.CreateEditor();
        foreach( var n in names )
        {
            if( e.GitRepository.Repository.Branches[$"origin/{n}"] != null ) e.GitRepository.Repository.Branches.Remove( $"origin/{n}", isRemote: true );
        }
    }

    static string ReadTags( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                    .Select( t => t.FriendlyName )
                    .Where( n => n != "ckli-repo" )
                    .Order( StringComparer.Ordinal )
                    .Concatenate();
    }

    static string[] RemoteTags( FakeBuildStack stack, string repoName )
    {
        using var remote = new LibGit2Sharp.Repository( stack.Remotes.GetUriFor( repoName ).LocalPath );
        return remote.Tags.Select( t => t.FriendlyName ).ToArray();
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
