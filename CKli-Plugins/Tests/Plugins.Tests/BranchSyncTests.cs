using CK.Core;
using CKli;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli branch sync" integrates into a branch what its link propagates from its closest existing parent (see
/// <see cref="CKli.BranchModel.Plugin.HotBranch.GetLinkCommit"/>): a "Full" link the parent's "dev/" tip, a "CI"
/// link the parent's last built commit (CI builds included) and a "Release" link its last released one. The merge
/// always targets the "dev/" branch and the optional mode overrides the configured link type.
/// <para>
/// These tests use the fake build harness only. As in <see cref="BranchStartCommitTests"/>, each link type has its
/// own branch name (a name carries its link type in the World's BranchNamespace).
/// </para>
/// </summary>
public class BranchSyncTests
{
    /// <summary>
    /// A "Full" link follows the parent's "dev/" branch: a new commit there is fast-forwarded into the child when
    /// the child has nothing of its own, and merged with a merge commit when it has. Synchronizing an up to date
    /// branch changes nothing.
    /// </summary>
    [Test]
    public async Task a_Full_link_integrates_the_parent_dev_branch_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();

        // Nothing of its own: the parent's new commit is fast-forwarded.
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable" );
        var parentTip = BranchTip( r, "dev/stable" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeTrue();
        WorkTip( r, "sierra" ).ShouldBe( parentTip, "Fast-forwarded to the parent's \"dev/\" tip." );
        // The useless "dev/sierra" that "branch open" left checked out has been auto fixed (deleted, which checks
        // out "sierra") and recreated by the merge: the work goes on in "dev/sierra", never on "sierra".
        CurrentBranch( r ).ShouldBe( "dev/sierra" );

        // A change of its own (on the checked out branch) and a new commit on the parent: a merge commit joins them.
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: null, fileName: "Sierra.txt" );
        var ownTip = WorkTip( r, "sierra" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable" );
        parentTip = BranchTip( r, "dev/stable" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeTrue();
        var merged = WorkTip( r, "sierra" );
        ParentsOf( r, merged ).ShouldBe( [ownTip, parentTip], ignoreOrder: true );

        // Up to date: nothing moves.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeTrue();
        WorkTip( r, "sierra" ).ShouldBe( merged );
    }

    /// <summary>
    /// A "CI" link only receives built commits: an unbuilt commit of the parent is not integrated until a CI build
    /// of the parent covers it.
    /// </summary>
    [Test]
    public async Task a_CI_link_integrates_the_last_built_commit_of_the_parent_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var unbuilt = await BuildOnStableThenLeaveAnUnbuiltCommitAsync( r ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo", "--link", "CI" )).ShouldBeTrue();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "romeo" )).ShouldBeTrue();
        Contains( r, "romeo", unbuilt ).ShouldBeFalse( "The unbuilt commit of 'stable' is not propagated." );

        await CIBuildStableAsync( r ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "romeo" )).ShouldBeTrue();
        Contains( r, "romeo", unbuilt ).ShouldBeTrue( "The CI build of 'stable' covers it: it is propagated." );
    }

    /// <summary>
    /// A "Release" link ignores the CI builds of its parent. The mode overrides the configured link type: a "Full"
    /// synchronization of the same branch integrates the parent's tip, built or not.
    /// </summary>
    [Test]
    public async Task a_Release_link_ignores_CI_builds_unless_the_mode_overrides_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var unbuilt = await BuildOnStableThenLeaveAnUnbuiltCommitAsync( r ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "quebec", "--link", "Release" )).ShouldBeTrue();

        await CIBuildStableAsync( r ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "quebec" )).ShouldBeTrue();
        Contains( r, "quebec", unbuilt ).ShouldBeFalse( "A CI build is not a release." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "quebec", "--mode", "Full" )).ShouldBeTrue();
        Contains( r, "quebec", unbuilt ).ShouldBeTrue( "Full integrates the parent's tip." );
    }

    /// <summary>
    /// A conflicting merge is computed without touching anything: the command fails, says that this must be fixed
    /// manually and the branch stays where it was.
    /// </summary>
    [Test]
    public async Task a_conflict_fails_and_leaves_the_branch_unchanged_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        var before = MakeConflict( r );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra", "--fail-on-conflict" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Failed merging branch 'dev/stable' into 'dev/sierra'" )
                                     && l.Contains( "This must be fixed manually." ) );
        }
        WorkTip( r, "sierra" ).ShouldBe( before );
    }

    /// <summary>
    /// With --all, a repository that fails does not stop the others: they are synchronized and the command fails.
    /// </summary>
    [Test]
    public async Task with_all_a_conflict_does_not_prevent_the_other_repositories_from_being_synchronized_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rConflict = await world.CreateRepoAsync( "X-Conflict", "v1.0.1" ).ConfigureAwait( false );
        var rClean = await world.CreateRepoAsync( "X-Clean", "v1.0.1" ).ConfigureAwait( false );
        foreach( var r in new[] { rConflict, rClean } )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        }
        var conflictBefore = MakeConflict( rConflict );
        TestHelper.TouchAndCommit( rClean.WorkingFolderPath, branchName: "dev/stable" );
        var cleanParentTip = BranchTip( rClean, "dev/stable" );

        // From one repository: --all is what brings the other one in.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConflict.Root, "branch", "sync", "sierra", "--all", "--fail-on-conflict" )).ShouldBeFalse();

        WorkTip( rConflict, "sierra" ).ShouldBe( conflictBefore );
        WorkTip( rClean, "sierra" ).ShouldBe( cleanParentTip );
    }

    /// <summary>
    /// A repository with issues is skipped, and a skipped repository is a failure: the branch has not been
    /// synchronized there. Here "sierra" and "dev/sierra" both have a commit of their own (they are desynchronized).
    /// </summary>
    [Test]
    public async Task a_repository_with_issues_fails_the_command_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/sierra" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "sierra", fileName: "OnBase.txt" );
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: "dev/stable" );
        var before = WorkTip( r, "sierra" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository 'X-Core' has issues: branch 'sierra' has not been synchronized." ) );
        }
        WorkTip( r, "sierra" ).ShouldBe( before );
    }

    /// <summary>
    /// A "Manual" link propagates nothing, so there is nothing to synchronize it with.
    /// </summary>
    [Test]
    public async Task the_mode_cannot_be_Manual_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra", "--mode", "Manual" )).ShouldBeFalse();
            logs.ShouldContain( "Invalid link type 'Manual'. Must be Release, CI or Full." );
        }
    }

    /// <summary>
    /// The real life case: two branches built independently both rewrite the references to the World's packages, so
    /// merging the parent's build into the child conflicts on the very same &lt;PackageReference Version="..." /&gt;
    /// line. The version is resolved the way a build of the child would update it: X-App keeps the "juliet" build of
    /// X-Core. "ckli branch list" announces it as a merge, not as a conflict.
    /// </summary>
    [Test]
    public async Task conflicting_package_versions_are_resolved_like_a_build_of_the_branch_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var (core, app) = await ArrangeVersionConflictAsync( world ).ConfigureAwait( false );
        var julietVersion = Reference( app, "dev/juliet", core.DefaultProjectName );
        Reference( app, "dev/stable", core.DefaultProjectName ).ShouldNotBe( julietVersion, "Both branches have rewritten the reference." );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "list" )).ShouldBeTrue();
        display.ToString().ShouldContain( "2 merges" );
        display.ToString().ShouldNotContain( "conflict" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "sync", "juliet", "--all" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "into 'dev/juliet' in 'X-App' aligned 1 package version(s):" ) );
        }
        Reference( app, "dev/juliet", core.DefaultProjectName ).ShouldBe( julietVersion );
        ParentsOf( app, WorkTip( app, "juliet" ) ).Length.ShouldBe( 2, "A merge commit." );
        Contains( app, "juliet", WorkTip( app, "stable" ) ).ShouldBeTrue( "The parent's CI build is merged." );
    }

    /// <summary>
    /// A synchronized CI link merges a build of its parent: the merge commit has version tags on both of its sides.
    /// The tag commit tree of such a history (VersionTagInfo.HotZoneInfo.CreateTagCommitTree) is walked breadth-first
    /// by level, so that the closest build of "juliet" is found on both sides and the branch's own one wins. Here X-Core
    /// is synchronized alone (no conflict, no resolver): a CI build of "juliet" still produces X-Core's next "juliet"
    /// version, not one derived from the "stable" CI build it merged.
    /// </summary>
    [Test]
    public async Task the_closest_build_of_a_synchronized_CI_link_is_its_own_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var (core, app) = await ArrangeVersionConflictAsync( world ).ConfigureAwait( false );
        var julietVersion = Reference( app, "dev/juliet", core.DefaultProjectName );
        julietVersion.ShouldContain( "-juliet." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, core.Root, "branch", "sync", "juliet" )).ShouldBeTrue();
        ParentsOf( core, WorkTip( core, "juliet" ) ).Length.ShouldBe( 2, "A merge commit: tags on both sides." );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldContain( "-juliet." );
        display.ToString().ShouldNotContain( "--ci." );
    }

    /// <summary>
    /// Aligning the package versions removes their conflicts only: a project file that also conflicts elsewhere is
    /// a real conflict and the merge fails.
    /// </summary>
    [Test]
    public async Task a_project_file_conflict_beyond_package_versions_fails_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var (_, app) = await ArrangeVersionConflictAsync( world ).ConfigureAwait( false );
        var projectFolder = app.WorkingFolderPath.AppendPart( app.DefaultProjectName );
        var projectFile = app.DefaultProjectName + ".csproj";
        TestHelper.TouchAndCommit( projectFolder, "dev/juliet", fileContent: s => s!.Replace( "</Project>", "<!-- juliet --></Project>" ), fileName: projectFile );
        TestHelper.TouchAndCommit( projectFolder, "dev/stable", fileContent: s => s!.Replace( "</Project>", "<!-- stable --></Project>" ), fileName: projectFile );
        // A CI link only merges built commits: "stable" must be built again.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "juliet" )).ShouldBeTrue();
        var before = WorkTip( app, "juliet" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "sync", "juliet", "--all", "--fail-on-conflict" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "into 'dev/juliet' in 'X-App'. Beyond the package versions, conflicts in:" )
                                     && l.Contains( $"{app.DefaultProjectName}/{projectFile}" ) );
        }
        WorkTip( app, "juliet" ).ShouldBe( before );
    }

    /// <summary>
    /// X-Core &lt;- X-App released on "stable", "juliet" opened as a CI link, then a CI build of "juliet" and a CI build
    /// of "stable" (each one with a change in X-Core): both rewrite the X-Core reference of X-App, "dev/juliet" is
    /// checked out.
    /// </summary>
    internal static async Task<(FakeBuildRepo Core, FakeBuildRepo App)> ArrangeVersionConflictAsync( FakeBuildWorld world,
                                                                                            Action<FakeBuildRepo>? onAppBeforeSwitchingToJuliet = null,
                                                                                            bool withTemplateProject = false )
    {
        var core = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var app = await world.CreateRepoAsync( "X-App", "v1.0.0", default, core ).ConfigureAwait( false );
        if( withTemplateProject )
        {
            // A project file that is not in the solution: a template whose references are placeholders.
            // Its folder comes before the solution's project in the tree.
            Directory.CreateDirectory( app.WorkingFolderPath.AppendPart( "A-Templates" ) );
            TestHelper.TouchAndCommit( app.WorkingFolderPath.AppendPart( "A-Templates" ),
                                       branchName: null,
                                       fileContent: _ => $"""
                                                          <Project Sdk="Microsoft.NET.Sdk">
                                                            <ItemGroup>
                                                              <PackageReference Include="{core.DefaultProjectName}" Version="0.0.0-placeholder" />
                                                            </ItemGroup>
                                                          </Project>
                                                          """,
                                       fileName: "Template.csproj" );
        }
        TestHelper.TouchAndCommit( core.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" ).ConfigureAwait( false )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "juliet", "--link", "CI" ).ConfigureAwait( false )).ShouldBeTrue();

        TestHelper.TouchAndCommit( core.WorkingFolderPath, branchName: null, fileName: "Juliet.txt" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" ).ConfigureAwait( false )).ShouldBeTrue();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" ).ConfigureAwait( false )).ShouldBeTrue();
        TestHelper.TouchAndCommit( core.WorkingFolderPath, branchName: null, fileName: "Stable.txt" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" ).ConfigureAwait( false )).ShouldBeTrue();

        // "dev/juliet" is not checked out here: it can be changed directly.
        onAppBeforeSwitchingToJuliet?.Invoke( app );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "juliet" ).ConfigureAwait( false )).ShouldBeTrue();
        return (core, app);
    }

    /// <summary>
    /// A project renamed on one side and whose version is updated on the other: the alignment applies to all the
    /// project files of each side, so git merges the rename and the aligned version like any rename.
    /// </summary>
    [Test]
    public async Task a_renamed_project_with_conflicting_package_versions_is_merged_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var (core, app) = await ArrangeVersionConflictAsync( world, app => MoveProjectFolder( app, "dev/juliet", "Src" ) ).ConfigureAwait( false );
        var julietVersion = Reference( app, "dev/juliet", core.DefaultProjectName );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "sync", "juliet", "--all" )).ShouldBeTrue();

        Reference( app, "dev/juliet", core.DefaultProjectName ).ShouldBe( julietVersion );
        using var e = app.CreateEditor();
        var tree = e.GitRepository.Repository.Branches["dev/juliet"].Tip.Tree;
        tree[$"Src/{app.DefaultProjectName}/{app.DefaultProjectName}.csproj"].ShouldNotBeNull();
        tree[app.DefaultProjectName].ShouldBeNull( "The project stays where juliet moved it." );
    }

    /// <summary>
    /// The versions that are aligned are the ones of the solution's project files: a project file that is not in the
    /// solution (a template with placeholder versions) is neither read nor rewritten.
    /// </summary>
    [Test]
    public async Task a_project_file_outside_the_solution_is_ignored_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var (core, app) = await ArrangeVersionConflictAsync( world, withTemplateProject: true ).ConfigureAwait( false );
        var julietVersion = Reference( app, "dev/juliet", core.DefaultProjectName );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "sync", "juliet", "--all" )).ShouldBeTrue();

        Reference( app, "dev/juliet", core.DefaultProjectName ).ShouldBe( julietVersion );
        using var e = app.CreateEditor();
        var template = (Blob)e.GitRepository.Repository.Branches["dev/juliet"].Tip.Tree["A-Templates/Template.csproj"].Target;
        template.GetContentText().ShouldContain( "Version=\"0.0.0-placeholder\"" );
    }

    // Moves the default project folder of a repository under a folder, on a branch that is not checked out.
    static void MoveProjectFolder( FakeBuildRepo repo, string branchName, string under )
    {
        using var e = repo.CreateEditor();
        var git = e.GitRepository.Repository;
        var branch = git.Branches[branchName];
        var tip = branch.Tip;
        var name = repo.DefaultProjectName;
        var definition = TreeDefinition.From( tip );
        definition.Remove( name );
        foreach( var entry in (Tree)tip.Tree[name].Target )
        {
            definition.Add( $"{under}/{name}/{entry.Name}", entry );
        }
        var slnx = tip.Tree[repo.SolutionFileName];
        var solution = ((Blob)slnx.Target).GetContentText().Replace( $"\"{name}/", $"\"{under}/{name}/" );
        definition.Add( repo.SolutionFileName, git.ObjectDatabase.CreateBlob( new MemoryStream( Encoding.UTF8.GetBytes( solution ) ) ), slnx.Mode );
        var signature = new Signature( "CKli.Testing", "none", DateTimeOffset.Now );
        var commit = git.ObjectDatabase.CreateCommit( signature, signature, $"Moving '{name}' to '{under}'.", git.ObjectDatabase.CreateTree( definition ), [tip], prettifyMessage: false );
        git.Refs.UpdateTarget( branch.Reference, commit.Id );
    }

    // The version a project of a branch references.
    internal static string Reference( FakeBuildRepo repo, string branchName, string packageId )
    {
        using var e = repo.CreateEditor();
        var p = e.ReadProjects( branchName ).Single( x => x.ProjectName == repo.DefaultProjectName );
        return p.References.Single( x => x.PackageId == packageId ).Version.ToString();
    }

    /// <summary>
    /// By default, a conflict is left in progress in the working folder: "dev/sierra" is checked out with a merge in
    /// progress that "ckli status" shows. Once resolved and committed (here with LibGit2Sharp, like any Git tool would),
    /// the merge commit has the two original commits as parents and the branch is synchronized.
    /// </summary>
    [Test]
    public async Task by_default_a_conflict_is_left_in_progress_in_the_working_folder_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "sierra", "--link", "Full" )).ShouldBeTrue();
        var before = MakeConflict( r );
        var stableTip = BranchTip( r, "dev/stable" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeFalse();
        display.ToString().ShouldBe( """
            A merge is left in progress: resolve its conflicts and commit it (or abort it).
            > X-Core  ⎇ dev/sierra ← branch 'dev/stable'  1 conflict
            │ Conflict.txt
            ❌ Failed

            """ );
        WorkTip( r, "sierra" ).ShouldBe( before, "Nothing is committed." );
        using( var e = r.CreateEditor() )
        {
            var status = e.GitRepository.GetSimpleStatusInfo();
            status.CurrentBranchName.ShouldBe( "dev/sierra" );
            status.Operation.ShouldBe( CurrentOperation.Merge );
            status.ConflictCount.ShouldBe( 1 );
        }

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "status" )).ShouldBeTrue();
        display.ToString().ShouldContain( "(merging, 1 conflict)" );

        File.WriteAllText( r.WorkingFolderPath.AppendPart( "Conflict.txt" ), "resolved" );
        using( var e = r.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            Commands.Stage( git, "Conflict.txt" );
            var signature = new Signature( "CKli.Testing", "none", DateTimeOffset.Now );
            git.Commit( "Merged branch 'dev/stable'.", signature, signature );
        }
        ParentsOf( r, WorkTip( r, "sierra" ) ).ShouldBe( [before, stableTip] );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "sync", "sierra" )).ShouldBeTrue();
    }

    /// <summary>
    /// The prepared merge has its package versions aligned: in the project file that also conflicts elsewhere, the
    /// X-Core reference already is the "juliet" one and the only conflict markers are around the real conflict.
    /// </summary>
    [Test]
    public async Task the_merge_left_in_progress_has_its_package_versions_aligned_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var (core, app) = await ArrangeVersionConflictAsync( world ).ConfigureAwait( false );
        var projectFolder = app.WorkingFolderPath.AppendPart( app.DefaultProjectName );
        var projectFile = app.DefaultProjectName + ".csproj";
        TestHelper.TouchAndCommit( projectFolder, "dev/juliet", fileContent: s => s!.Replace( "</Project>", "<!-- juliet --></Project>" ), fileName: projectFile );
        TestHelper.TouchAndCommit( projectFolder, "dev/stable", fileContent: s => s!.Replace( "</Project>", "<!-- stable --></Project>" ), fileName: projectFile );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "switch", "juliet" )).ShouldBeTrue();
        var julietVersion = Reference( app, "dev/juliet", core.DefaultProjectName );
        var stableVersion = Reference( app, "dev/stable", core.DefaultProjectName );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "sync", "juliet", "--all" )).ShouldBeFalse();

        using( var e = app.CreateEditor() )
        {
            var status = e.GitRepository.GetSimpleStatusInfo();
            status.Operation.ShouldBe( CurrentOperation.Merge );
            status.ConflictCount.ShouldBe( 1 );
        }
        var text = File.ReadAllText( projectFolder.AppendPart( projectFile ) );
        text.ShouldContain( $"Include=\"{core.DefaultProjectName}\" Version=\"{julietVersion}\"" );
        text.ShouldNotContain( stableVersion );
        text.ShouldContain( "<<<<<<<" );
        text.ShouldContain( "<!-- juliet -->" );
        text.ShouldContain( "<!-- stable -->" );
    }

    /// <summary>
    /// Commits the same new file with different contents on "dev/sierra" and on "dev/stable".
    /// </summary>
    /// <returns>The sha of the "dev/sierra" tip.</returns>
    static string MakeConflict( FakeBuildRepo repo )
    {
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "dev/sierra", fileContent: _ => "sierra", fileName: "Conflict.txt" );
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "dev/stable", fileContent: _ => "stable", fileName: "Conflict.txt" );
        return WorkTip( repo, "sierra" );
    }

    /// <summary>
    /// Builds the repository so that "stable" carries a release, then leaves an unbuilt commit on "stable".
    /// </summary>
    /// <returns>The sha of the unbuilt commit.</returns>
    static async Task<string> BuildOnStableThenLeaveAnUnbuiltCommitAsync( FakeBuildRepo repo )
    {
        // A change on "dev/stable" (the checked out branch of a fresh repository) and a build: the build
        // integrates "dev/stable" into "stable" and tags it.
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "build", "--release" ).ConfigureAwait( false )).ShouldBeTrue();
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "stable" );
        return BranchTip( repo, "stable" );
    }

    /// <summary>
    /// Switches back to "stable" and builds it in CI.
    /// </summary>
    static async Task CIBuildStableAsync( FakeBuildRepo repo )
    {
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "branch", "switch", "stable" ).ConfigureAwait( false )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "build" ).ConfigureAwait( false )).ShouldBeTrue();
    }

    internal static string BranchTip( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        var b = e.GitRepository.Repository.Branches[branchName];
        b.ShouldNotBeNull( $"Branch '{branchName}' not found in '{repo.DisplayPath}'." );
        return b.Tip.Sha;
    }

    /// <summary>
    /// Gets the tip of the branch the work of <paramref name="branchName"/> is on: its "dev/" branch when it exists
    /// (a synchronization merges there), the branch itself otherwise.
    /// </summary>
    static string WorkTip( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        var git = e.GitRepository.Repository;
        var b = git.Branches["dev/" + branchName] ?? git.Branches[branchName];
        b.ShouldNotBeNull( $"Branch '{branchName}' not found in '{repo.DisplayPath}'." );
        return b.Tip.Sha;
    }

    static string CurrentBranch( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Head.FriendlyName;
    }

    internal static string[] ParentsOf( FakeBuildRepo repo, string sha )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Lookup<Commit>( sha ).Parents.Select( p => p.Sha ).ToArray();
    }

    static bool Contains( FakeBuildRepo repo, string branchName, string sha )
    {
        var tip = WorkTip( repo, branchName );
        using var e = repo.CreateEditor();
        var git = e.GitRepository.Repository;
        return git.ObjectDatabase.FindMergeBase( git.Lookup<Commit>( tip ), git.Lookup<Commit>( sha ) )?.Sha == sha;
    }
}
