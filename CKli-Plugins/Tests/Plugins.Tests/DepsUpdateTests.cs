using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
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
    /// Applying is not implemented yet: without --dry-run the command reports and fails rather than pretending.
    /// </summary>
    [Test]
    public async Task applying_is_not_implemented_yet_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "deps", "update" )).ShouldBeFalse();
    }
}
