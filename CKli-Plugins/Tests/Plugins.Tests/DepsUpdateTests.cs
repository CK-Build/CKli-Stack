using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli deps update --dry-run": the external package upgrades that would align a World.
/// <para>
/// These use the fake build harness and its "file://" folder feeds, which
/// <see cref="CKli.Core.NuGetHelper.EnsureLocalFeed"/> seeds with "ck.canarypackage/1.0.0" - so the target
/// version a feed offers is deterministic and offline.
/// </para>
/// </summary>
public class DepsUpdateTests
{
    /// <summary>
    /// No reference anchors "CK.CanaryPackage", so the greatest version the World's feeds offer is the
    /// target: the repository that references an older one is reported.
    /// </summary>
    [Test]
    public async Task an_external_package_behind_the_feed_is_reported_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "X-Core" );
        text.ShouldContain( "CK.CanaryPackage 0.9.0 → 1.0.0" );
        text.ShouldContain( "1 upgrade(s) in 1 repositories" );
    }

    /// <summary>
    /// A World already on the version its feeds offer has nothing to update. A package no feed knows is not
    /// an error either: there is simply no target for it.
    /// </summary>
    [Test]
    public async Task an_aligned_World_has_nothing_to_update_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "1.0.0" ) );
            e.AddOrUpdateReference( rCore.DefaultProjectName, "No.Such.Package", SVersion.Parse( "3.2.1" ) );
        }

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Nothing to update" );
    }

    /// <summary>
    /// An upstream that must be updated joins the pivots - it will be rebuilt - and by default so do the
    /// downstreams of an updated repository: that is the "wide update" guaranty, and it is exactly what the
    /// build that follows would touch. "--narrow" keeps the update to the pivots and their upstreams.
    /// </summary>
    [Test]
    public async Task the_upgrades_reach_the_upstreams_and_by_default_the_downstreams_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        // A chain: X-Core <- X-Middle (the pivot) <- X-Sample.
        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rMiddle = await world.CreateRepoAsync( "X-Middle", "v0.3.3", references: [rCore] ).ConfigureAwait( false );
        var rSample = await world.CreateRepoAsync( "X-Sample", "v0.0.0", references: [rMiddle] ).ConfigureAwait( false );
        // All three are behind on the same external package.
        foreach( var r in new[] { rCore, rMiddle, rSample } )
        {
            using var e = r.CreateEditor();
            e.AddOrUpdateReference( r.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }

        // From the middle repository: its upstream (X-Core) and its downstream (X-Sample) are both reported.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rMiddle.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        var wide = display.ToString();
        wide.ShouldContain( "X-Core" );
        wide.ShouldContain( "X-Middle" );
        wide.ShouldContain( "X-Sample" );
        wide.ShouldContain( "3 upgrade(s) in 3 repositories" );

        // --narrow: the downstream is left out.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rMiddle.Root, "deps", "update", "--dry-run", "--narrow" )).ShouldBeTrue();
        var narrow = display.ToString();
        narrow.ShouldContain( "X-Core" );
        narrow.ShouldContain( "X-Middle" );
        narrow.ShouldNotContain( "X-Sample" );
        narrow.ShouldContain( "2 upgrade(s) in 2 repositories" );
    }

    /// <summary>
    /// A pin in the World's &lt;VersionTag&gt;&lt;Packages&gt; configuration is an authoritative exception:
    /// the identifier has no target at all, so it is never upgraded.
    /// </summary>
    [Test]
    public async Task a_pinned_package_is_never_upgraded_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: ( monitor, stackPath, plugins ) =>
        {
            Helper.ConfigureFakeFeeds( monitor, stackPath, plugins );
            var versionTag = plugins.Element( "VersionTag" ).ShouldNotBeNull();
            versionTag.Add( new System.Xml.Linq.XElement( "Packages",
                                new System.Xml.Linq.XElement( "Package",
                                    new System.Xml.Linq.XAttribute( "Name", "CK.CanaryPackage" ),
                                    new System.Xml.Linq.XAttribute( "Version", "0.9.0" ) ) ) );
        } ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Nothing to update" );
    }

    /// <summary>
    /// A World Reference is the source that matters: the version its published profile carries for a package
    /// it produces is the target, and it wins over what the feeds offer. The referenced Stack here publishes
    /// "R.Lib" at 1.0.2 while the feed offers 9.9.9: the target is the reference's version.
    /// </summary>
    [Test]
    public async Task a_World_Reference_anchors_the_target_and_wins_over_the_feeds_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        // The referenced Stack: a repository, a change and a publication - so it has a Published/index.json
        // and a profile that carries its produced package.
        var refStack = await testEnv.CreateStackAsync( "Ref", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rLib = await refStack.DefaultWorld.CreateRepoAsync( "R-Lib", "v1.0.1" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rLib.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, refStack.DefaultWorld.WorldRoot, "publish" )).ShouldBeTrue();

        // The consuming Stack: it is behind on the referenced package.
        var stack = await testEnv.CreateStackAsync( "Test", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;
        var rApp = await world.CreateRepoAsync( "X-App", "v0.1.0" ).ConfigureAwait( false );
        using( var e = rApp.CreateEditor() )
        {
            e.AddOrUpdateReference( rApp.DefaultProjectName, "R.Lib", SVersion.Parse( "1.0.1" ) );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "reference", "set", refStack.Remotes.StackUri.ToString() )).ShouldBeTrue();

        // The feed offers a greater version of the very same package: an empty version folder is all the V3
        // expanded layout needs to list it.
        var (nugetOrgFeed, _) = Helper.GetFakeFeedPaths( testEnv.Path );
        Directory.CreateDirectory( Path.Combine( nugetOrgFeed, "r.lib", "9.9.9" ) );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "R.Lib 1.0.1 → 1.0.2", customMessage: text );
        text.ShouldContain( "produced by", customMessage: "The origin is the reference's profile, not a feed." );
        text.ShouldNotContain( "9.9.9", customMessage: "The feed does not win over a reference." );
    }

    /// <summary>
    /// "--ci" considers the CI published profiles of the References. A folder holds at most one alive CI
    /// profile per branch and it is newer than every non CI publication of that branch, so the one that is
    /// there simply applies: this asserts both halves - without the flag the release is the target, with it
    /// the CI publication is.
    /// </summary>
    [Test]
    public async Task the_CI_published_profile_of_a_Reference_is_used_only_with_ci_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        var refStack = await testEnv.CreateStackAsync( "Ref", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rLib = await refStack.DefaultWorld.CreateRepoAsync( "R-Lib", "v1.0.1" ).ConfigureAwait( false );
        // A release, then a CI publication on top of it.
        TestHelper.TouchAndCommit( rLib.WorkingFolderPath, branchName: null, fileName: "First.txt" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, refStack.DefaultWorld.WorldRoot, "publish" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rLib.WorkingFolderPath, branchName: null, fileName: "Second.txt" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, refStack.DefaultWorld.WorldRoot, "publish", "--ci" )).ShouldBeTrue();

        var stack = await testEnv.CreateStackAsync( "Test", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;
        var rApp = await world.CreateRepoAsync( "X-App", "v0.1.0" ).ConfigureAwait( false );
        using( var e = rApp.CreateEditor() )
        {
            e.AddOrUpdateReference( rApp.DefaultProjectName, "R.Lib", SVersion.Parse( "1.0.1" ) );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "reference", "set", refStack.Remotes.StackUri.ToString() )).ShouldBeTrue();

        // Without --ci: the release is the target, the CI publication is not a candidate.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        var release = display.ToString();
        release.ShouldContain( "R.Lib 1.0.1 → 1.0.2", customMessage: release );

        // With --ci: the CI publication of the very same branch applies.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--dry-run", "--ci" )).ShouldBeTrue();
        var ci = display.ToString();
        ci.ShouldContain( "R.Lib 1.0.1 → 1.0.3--ci", customMessage: ci );
    }

    /// <summary>
    /// Applying rewrites the project on the "dev/" branch and commits it: a second run has nothing left to do.
    /// </summary>
    [Test]
    public async Task applying_rewrites_the_project_and_commits_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }
        var before = DevStableTip( rCore );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Updated 1 package(s) in 1 repositories" );

        // The reference has been rewritten in the project of the "dev/" branch, and committed.
        Reference( rCore, "dev/stable", "CK.CanaryPackage" ).ShouldBe( "1.0.0" );
        DevStableTip( rCore ).ShouldNotBe( before, "The update has been committed." );

        // Idempotent: the World is aligned now.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Nothing to update" );
    }

    /// <summary>
    /// A repository that doesn't have the branch gets it created - at the commit its BranchLinkType says,
    /// which is the content that has been analyzed - and the update is committed on its "dev/" branch.
    /// </summary>
    [Test]
    public async Task applying_creates_the_branch_of_a_repository_that_lacks_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        // X-Core <- X-App (the pivot). Both are behind on the same external package.
        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rApp = await world.CreateRepoAsync( "X-App", "v0.1.0", references: [rCore] ).ConfigureAwait( false );
        foreach( var r in new[] { rCore, rApp } )
        {
            using var e = r.CreateEditor();
            e.AddOrUpdateReference( r.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }
        // "romeo" is opened on the pivot only: X-Core doesn't have it, but it is in the World branch model.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();
        BranchExists( rCore, "romeo" ).ShouldBeFalse();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--branch", "romeo" )).ShouldBeTrue();

        // The upstream joined the update and its "romeo" branch has been created.
        BranchExists( rCore, "romeo" ).ShouldBeTrue();
        Reference( rCore, "dev/romeo", "CK.CanaryPackage" ).ShouldBe( "1.0.0" );
        Reference( rApp, "dev/romeo", "CK.CanaryPackage" ).ShouldBe( "1.0.0" );
        // Only the analyzed branch is updated: "dev/stable" still references the old version.
        Reference( rCore, "dev/stable", "CK.CanaryPackage" ).ShouldBe( "0.9.0" );
    }

    /// <summary>
    /// A downgrade is applied only with --allow-downgrade: a World Reference may pin lower than what this
    /// World references and alignment is the point, but it is never a silent move.
    /// </summary>
    [Test]
    public async Task a_downgrade_requires_allow_downgrade_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        var refStack = await testEnv.CreateStackAsync( "Ref", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rLib = await refStack.DefaultWorld.CreateRepoAsync( "R-Lib", "v1.0.1" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rLib.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, refStack.DefaultWorld.WorldRoot, "publish" )).ShouldBeTrue();

        var stack = await testEnv.CreateStackAsync( "Test", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;
        var rApp = await world.CreateRepoAsync( "X-App", "v0.1.0" ).ConfigureAwait( false );
        // Ahead of what the reference publishes (1.0.2): aligning on it moves the version DOWN.
        using( var e = rApp.CreateEditor() )
        {
            e.AddOrUpdateReference( rApp.DefaultProjectName, "R.Lib", SVersion.Parse( "2.0.0" ) );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "reference", "set", refStack.Remotes.StackUri.ToString() )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update" )).ShouldBeFalse();
        display.ToString().ShouldContain( "R.Lib 2.0.0 \u2192 1.0.2" );
        Reference( rApp, "dev/stable", "R.Lib" ).ShouldBe( "2.0.0", "Nothing has been written." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--allow-downgrade" )).ShouldBeTrue();
        Reference( rApp, "dev/stable", "R.Lib" ).ShouldBe( "1.0.2" );
    }

    static bool BranchExists( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Branches[branchName] != null;
    }

    static string DevStableTip( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Branches["dev/stable"].Tip.Sha;
    }

    // The version a project of a branch references, or null when that identifier is not referenced.
    static string? Reference( FakeBuildRepo repo, string branchName, string packageId )
    {
        using var e = repo.CreateEditor();
        var p = e.ReadProjects( branchName ).Single( x => x.ProjectName == repo.DefaultProjectName );
        var found = p.References.Where( x => x.PackageId == packageId ).ToList();
        found.Count.ShouldBeLessThanOrEqualTo( 1, $"'{packageId}' is referenced more than once." );
        return found.Count == 0 ? null : found[0].Version.ToString();
    }
}
