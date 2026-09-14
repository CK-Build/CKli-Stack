using CK.Core;
using CKli;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
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
    /// No reference anchors "CK.CanaryPackage", so with --with-nuget the greatest version the World's feeds
    /// offer is the target: the repository that references an older one is reported.
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "X-Core" );
        // The upgrade rows are indented below their repository: a multi line TextBlock trims its lines,
        // so this only holds because each line is its own renderable with a left margin.
        text.ShouldContain( "    ▲ CK.CanaryPackage 0.9.0 → 1.0.0", customMessage: text );
        text.ShouldContain( "1 upgrade(s) in 1 repositories" );
    }

    /// <summary>
    /// The World References are the default source: without --with-nuget no feed is queried at all, so an
    /// identifier no reference anchors has no target and is left alone - even though the feed offers a
    /// greater version and the very same command finds it with the flag.
    /// </summary>
    [Test]
    public async Task the_feeds_are_not_consulted_unless_with_nuget_Async()
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

        // By default: this World has no <Reference> and no bound, so nothing can anchor a target - and it is said.
        display.Clear();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update" )).ShouldBeTrue();
            logs.ShouldContain( """
                This World has no <Reference>, no <VersionTag><Packages> bound and --with-nuget is not
                specified: nothing can anchor a target version, so there is nothing to update.
                """ );
        }
        display.ToString().ShouldContain( "Nothing to update" );
        Reference( rCore, "dev/stable", "CK.CanaryPackage" ).ShouldBe( "0.9.0", "Nothing has been written." );

        // --with-nuget: the feed answers and the very same World has an upgrade.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 → 1.0.0" );

        // The feed only filters are refused rather than silently ignored.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--stable" )).ShouldBeFalse();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--prerelease" )).ShouldBeFalse();
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rMiddle.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
        var wide = display.ToString();
        wide.ShouldContain( "X-Core" );
        wide.ShouldContain( "X-Middle" );
        wide.ShouldContain( "X-Sample" );
        wide.ShouldContain( "3 upgrade(s) in 3 repositories" );

        // --narrow: the downstream is left out.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rMiddle.Root, "deps", "update", "--dry-run", "--with-nuget", "--narrow" )).ShouldBeTrue();
        var narrow = display.ToString();
        narrow.ShouldContain( "X-Core" );
        narrow.ShouldContain( "X-Middle" );
        narrow.ShouldNotContain( "X-Sample" );
        narrow.ShouldContain( "2 upgrade(s) in 2 repositories" );
    }

    /// <summary>
    /// A &lt;VersionTag&gt;&lt;Packages&gt; bound caps what a source may propose: a "[Lock]"ed bound accepts its
    /// base version and nothing else, so the greatest version the feeds offer is refused - and the report says
    /// that the package is held back rather than silently skipping it.
    /// </summary>
    [Test]
    public async Task a_locked_bound_is_a_pin_that_no_feed_can_move_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: BoundConfiguration( "CK.CanaryPackage", "0.9.0[Lock]" ) )
                                 .ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }

        // The feed offers 1.0.0 (seeded) but the bound refuses it.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "Nothing to update", customMessage: text );
        text.ShouldContain( "1 package(s) are held back by the World <Packages> configuration:", customMessage: text );
        text.ShouldContain( "CK.CanaryPackage: 1.0.0", customMessage: text );
        text.ShouldContain( "is not in the configured bound 0.9.0[Lock]", customMessage: text );
    }

    /// <summary>
    /// The bound is an invariant of the World, not only a cap: a repository that references the package outside
    /// of it is brought back to the bound's base version - even with no &lt;Reference&gt; and no --with-nuget,
    /// where no source can anchor anything at all.
    /// </summary>
    [Test]
    public async Task a_bound_alone_brings_an_out_of_bound_dependency_back_into_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: BoundConfiguration( "CK.CanaryPackage", "1.2.0[Lock]" ) )
                                 .ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }

        display.Clear();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update" )).ShouldBeTrue();
            logs.ShouldContain( """
                This World has no <Reference> and --with-nuget is not specified: nothing can anchor a target
                version, so only the <VersionTag><Packages> bounds can drive an update.
                """ );
        }
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 → 1.2.0", customMessage: display.ToString() );
        Reference( rCore, "dev/stable", "CK.CanaryPackage" ).ShouldBe( "1.2.0" );

        // Idempotent: the reference is in its bound now.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Nothing to update" );
    }

    /// <summary>
    /// A bare version is a floor and not a pin: a reference above it is left alone, one below it is brought up
    /// to it. This is what "Version" means now that it is a SVersionBound.
    /// </summary>
    [Test]
    public async Task a_bare_bound_is_a_floor_not_a_pin_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: BoundConfiguration( "CK.CanaryPackage", "1.0.0" ) )
                                 .ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        // Above the floor: nothing to do. A pin would have moved it back down to 1.0.0.
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "1.5.0" ) );
        }
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Nothing to update" );

        // Below the floor: brought up to it.
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 → 1.0.0", customMessage: display.ToString() );
    }

    /// <summary>
    /// The bound filters the candidates a feed offers instead of blocking on the greatest one: a "[LockMajor]"
    /// bound tracks the greatest version of that major and simply ignores the next one.
    /// </summary>
    [Test]
    public async Task a_LockMajor_bound_tracks_the_greatest_version_of_its_major_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: BoundConfiguration( "CK.CanaryPackage", "1.0.0[LockMajor]" ) )
                                 .ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( rCore.DefaultProjectName, "CK.CanaryPackage", SVersion.Parse( "0.9.0" ) );
        }
        // The feed offers 1.0.0 (seeded), 1.5.0 and 2.0.0: only the 1.x are candidates.
        SeedFeedVersion( testEnv, "ck.canarypackage", "1.5.0" );
        SeedFeedVersion( testEnv, "ck.canarypackage", "2.0.0" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "CK.CanaryPackage 0.9.0 → 1.5.0", customMessage: text );
        text.ShouldNotContain( "2.0.0", customMessage: "The greatest version is out of the bound: it is not a candidate." );
    }

    /// <summary>
    /// A World Reference is a source like any other as far as the bound is concerned: the version its published
    /// profile carries is refused when it is outside of it, and the package is reported as held back.
    /// </summary>
    [Test]
    public async Task a_bound_caps_what_a_World_Reference_may_propose_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        // The referenced Stack publishes "R.Lib" at 1.0.2.
        var refStack = await testEnv.CreateStackAsync( "Ref", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rLib = await refStack.DefaultWorld.CreateRepoAsync( "R-Lib", "v1.0.1" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rLib.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, refStack.DefaultWorld.WorldRoot, "publish" )).ShouldBeTrue();

        // The consuming Stack holds "R.Lib" at 1.0.1.
        var stack = await testEnv.CreateStackAsync( "Test", BoundConfiguration( "R.Lib", "1.0.1[Lock]" ) ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;
        var rApp = await world.CreateRepoAsync( "X-App", "v0.1.0" ).ConfigureAwait( false );
        using( var e = rApp.CreateEditor() )
        {
            e.AddOrUpdateReference( rApp.DefaultProjectName, "R.Lib", SVersion.Parse( "1.0.1" ) );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "reference", "set", refStack.Remotes.StackUri.ToString() )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--dry-run" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "Nothing to update", customMessage: text );
        text.ShouldContain( "held back by the World <Packages> configuration", customMessage: text );
        text.ShouldContain( "R.Lib: 1.0.2", customMessage: text );
        text.ShouldContain( "is not in the configured bound 1.0.1[Lock]", customMessage: text );
        Reference( rApp, "dev/stable", "R.Lib" ).ShouldBe( "1.0.1", "Nothing has been written." );
    }

    /// <summary>
    /// A Version that is not a version bound is a configuration error, and the command says which element
    /// carries it instead of failing later on something else.
    /// </summary>
    [Test]
    public async Task an_invalid_bound_is_a_configuration_error_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: BoundConfiguration( "CK.CanaryPackage", "not a bound" ) )
                                 .ConfigureAwait( false );
        var rCore = await stack.DefaultWorld.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run" )).ShouldBeFalse();
            logs.Any( l => l.Contains( "Unable to parse the Version attribute" ) ).ShouldBeTrue( logs.Concatenate( Environment.NewLine ) );
        }
    }

    // Configures the fake feeds and one <VersionTag><Packages><Package Name=".." Version=".." /> bound.
    static Action<IActivityMonitor, NormalizedPath, XElement> BoundConfiguration( string packageId, string bound )
    {
        return ( monitor, stackPath, plugins ) =>
        {
            Helper.ConfigureFakeFeeds( monitor, stackPath, plugins );
            var versionTag = plugins.Element( "VersionTag" ).ShouldNotBeNull();
            versionTag.Add( new XElement( "Packages",
                                new XElement( "Package",
                                    new XAttribute( "Name", packageId ),
                                    new XAttribute( "Version", bound ) ) ) );
        };
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--with-nuget" )).ShouldBeTrue();
        display.ToString().ShouldContain( "Updated 1 package(s) in 1 repositories" );

        // The reference has been rewritten in the project of the "dev/" branch, and committed.
        Reference( rCore, "dev/stable", "CK.CanaryPackage" ).ShouldBe( "1.0.0" );
        DevStableTip( rCore ).ShouldNotBe( before, "The update has been committed." );

        // Idempotent: the World is aligned now.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--with-nuget" )).ShouldBeTrue();
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "deps", "update", "--with-nuget", "--branch", "romeo" )).ShouldBeTrue();

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

    /// <summary>
    /// The version filter is stable/not and nothing finer: a root branch takes only the stable versions a
    /// feed offers, --prerelease takes the greatest whatever it is. The prerelease here is a plain SemVer
    /// one ("2.0.0-rc.1"), which is what most third party packages look like - and what CSVersionKindFilter
    /// would have rejected.
    /// </summary>
    [Test]
    public async Task the_root_branch_ignores_the_prereleases_of_a_feed_unless_prerelease_Async()
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
        // The feed offers 1.0.0 (seeded) and a greater prerelease.
        SeedFeedVersion( testEnv, "ck.canarypackage", "2.0.0-rc.1" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 \u2192 1.0.0", customMessage: display.ToString() );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget", "--prerelease" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 \u2192 2.0.0-rc.1", customMessage: display.ToString() );
    }

    /// <summary>
    /// The other way around on a prerelease branch: the prereleases are candidates there, and --stable
    /// restricts it back to the stable ones.
    /// </summary>
    [Test]
    public async Task a_prerelease_branch_takes_the_prereleases_unless_stable_Async()
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
        SeedFeedVersion( testEnv, "ck.canarypackage", "2.0.0-rc.1" );
        // A "Full" link: the default "CI" one would need a build on the parent to start from.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget", "--branch", "romeo" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 \u2192 2.0.0-rc.1", customMessage: display.ToString() );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget", "--branch", "romeo", "--stable" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 \u2192 1.0.0", customMessage: display.ToString() );

        // The two are exclusive.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget", "--stable", "--prerelease" )).ShouldBeFalse();
    }

    /// <summary>
    /// The command fetches first and then refuses a World that is behind its remotes: it requires a coherent
    /// World instead of merging one, so its report always describes what an update would write. --no-fetch
    /// skips the fetch, and the divergence is then simply not visible - which is exactly what the fetch buys.
    /// </summary>
    [Test]
    public async Task a_branch_behind_its_remote_is_refused_and_only_the_fetch_reveals_it_Async()
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
        // Someone else pushed to the remote: the local tracking branch is now behind it.
        CommitInRemote( rCore, "main" );

        // --no-fetch: the remote tracking reference is stale, so nothing looks wrong and the analysis runs.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget", "--no-fetch" )).ShouldBeTrue();
        display.ToString().ShouldContain( "CK.CanaryPackage 0.9.0 \u2192 1.0.0" );

        // With the fetch, the divergence is real and the command refuses rather than merging it.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update", "--dry-run", "--with-nuget" )).ShouldBeFalse();
        // Nothing has been written, and the local branch has NOT been merged.
        Reference( rCore, "dev/stable", "CK.CanaryPackage" ).ShouldBe( "0.9.0" );
        BehindBy( rCore, "main" ).ShouldBe( 1, "The command refuses, it never merges." );
    }

    // Adds a version of a package to the fake "nuget.org" feed: an empty folder is all the V3 expanded
    // layout needs to list it.
    static void SeedFeedVersion( FakeBuildTestEnv testEnv, string lowercasePackageId, string version )
    {
        var (nugetOrgFeed, _) = Helper.GetFakeFeedPaths( testEnv.Path );
        Directory.CreateDirectory( Path.Combine( nugetOrgFeed, lowercasePackageId, version ) );
    }

    // Commits directly in the remote (bare) repository: the local clone becomes behind it.
    static void CommitInRemote( FakeBuildRepo repo, string branchName )
    {
        var path = Repository.Discover( repo.World.Stack.Remotes.GetUriFor( repo.RepositoryName ).LocalPath );
        using var git = new Repository( path );
        var b = git.Branches[branchName].ShouldNotBeNull();
        var committer = new Signature( "SomeoneElse", "none", DateTimeOffset.Now );
        var c = git.ObjectDatabase.CreateCommit( committer, committer, "A commit pushed by someone else.",
                                                 b.Tip.Tree, [b.Tip], prettifyMessage: true );
        git.Refs.UpdateTarget( b.Reference, c.Id, null );
    }

    static int BehindBy( FakeBuildRepo repo, string branchName )
    {
        using var e = repo.CreateEditor();
        var b = e.GitRepository.Repository.Branches[branchName].ShouldNotBeNull();
        return b.TrackingDetails.BehindBy ?? 0;
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
