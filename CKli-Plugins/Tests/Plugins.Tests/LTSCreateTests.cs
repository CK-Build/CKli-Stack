using CK.Core;
using CKli;
using CKli.Core;
using CKli.Publish.Plugin;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli lts create" clones the default World's definition into a new Long Term Support World.
/// <para>
/// The VersionTag plugin handles the <see cref="WorldEvents.CreateLTS"/> event to cut the version range of
/// every repository in two: the new LTS World keeps the versions below the cut (its SupVersion) and the
/// default World is left with the ones above it (its InfVersion). Both bounds are exclusive, so the cut is
/// deliberately a "-0" prerelease: a version that no repository can ever produce, hence one that belongs to
/// no World rather than to both.
/// </para>
/// </summary>
public class LTSCreateTests
{
    /// <summary>
    /// The cut is the next Major of the version each repository currently offers, whatever that Major is: a
    /// v1.2.4 cuts at 2.0.0-0 and a v0.4.1 at 1.0.0-0 — the 0.x convention that treats the Minor as the
    /// breaking axis is deliberately NOT applied. The LTS World keeps its cloned InfVersion: only its
    /// SupVersion is set.
    /// </summary>
    [Test]
    public async Task lts_create_cuts_the_version_range_between_the_two_worlds_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.2.3" ).ConfigureAwait( false );
        var rApp = await world.CreateRepoAsync( "X-App", "v0.4.0", references: [rCore] ).ConfigureAwait( false );

        // The harness arranges the repositories' content on "dev/stable" and leaves "stable" behind: only an
        // actual publication integrates it, and an LTS cannot be created from a World that still has code in
        // its "dev/" branches. Touching X-Core drags its consumer along, so both are published.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core v1.2.3 → ⏚/v1.2.4 (CodeChange)   
            2 -  X-App  v0.4.0 → ⏚/v0.4.1 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeTrue();

        // The new World's definition file is a "{StackName}{LTSName}.xml" file in the Stack folder. Its root
        // element keeps the Stack name ('@' is not a valid XML name character) and carries the LTSName.
        var ltsRoot = LoadWorldDefinition( stack, "Test@net8.xml" );
        ltsRoot.Name.LocalName.ShouldBe( "Test" );
        ltsRoot.Attribute( "LTSName" )!.Value.ShouldBe( "@net8" );

        // The LTS World is bounded from above and keeps its (here absent) InfVersion.
        VersionBounds( ltsRoot ).ShouldBe( [("X-Core", null, "2.0.0-0"), ("X-App", null, "1.0.0-0")] );

        // The default World is bounded from below by the very same cut: the versions it has produced so far
        // now belong to the LTS World only.
        VersionBounds( LoadWorldDefinition( stack, "Test.xml" ) )
            .ShouldBe( [("X-Core", "2.0.0-0", null), ("X-App", "1.0.0-0", null)] );

        // The cut is usable: the default World has no version left, and the fix that the Build plugin offers
        // for it starts each repository exactly AT the cut (the "-0" prerelease is dropped). This is what an
        // InfVersion set on the cut's own Major.Minor.Patch would forbid.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldContain( "This can be fixed by creating a 'v2.0.0+fake' on 'stable' branch." );
        display.ToString().ShouldContain( "This can be fixed by creating a 'v1.0.0+fake' on 'stable' branch." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core v2.0.0+fake → ⏚/v2.0.0 (FakeVersion)               
            2 -  X-App  v1.0.0+fake → ⏚/v1.0.0 (UpstreamBuild, FakeVersion)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // The new World's branch model is cleared: only the root branch is kept. Its configuration holds the
        // name WITHOUT the "@net8/" prefix (BranchNamespace prepends it when reading), and the cloned <Explo>
        // elements are gone - they named branches this main line no longer has.
        var ltsBranchModel = ltsRoot.Element( "Plugins" )!.Element( "BranchModel" )!;
        ltsBranchModel.Attribute( "MainLine" )!.Value.ShouldBe( "stable" );
        ltsBranchModel.Elements( "Explo" ).ShouldBeEmpty();

        // And the new World can actually be opened: its repositories are cloned into the Stack's own "@net8/"
        // folder and its plugins instantiate. Its root branch is "@net8/stable" - it does not exist in the
        // repositories yet, which is the ordinary bootstrap state of a brand new LTS World.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "clone", "@net8" )).ShouldBeTrue();
        var ltsWorld = world.WorldRoot.ChangeDirectory( stack.StackRoot.AppendPart( "@net8" ) );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, ltsWorld, "issue" )).ShouldBeTrue();
        display.ToString().ShouldContain( "@net8/stable" );
    }

    /// <summary>
    /// A repository that has never published anything has no version to cut: the version it currently offers
    /// is the "v0.0.0+fake" that bootstraps it, then the "local/v0.0.0" that a build produces. Both are
    /// refused, and the refusal discards the InfVersion that was already set on the repositories handled
    /// before it: the default World's definition file is only saved when the command succeeds.
    /// </summary>
    [Test]
    public async Task lts_create_requires_every_repository_to_offer_a_published_version_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.2.3" ).ConfigureAwait( false );
        // No initial version: X-App keeps the "v0.0.0+fake" that the "Missing initial version" fix creates.
        var rApp = await world.CreateRepoAsync( "X-App", null, references: [rCore] ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository 'X-App' has a current fake or a deprecated version 'v0.0.0+fake'." ) );
            // The final error names the actual cause: it is not a catch-all "the world must be published".
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: a current fake or deprecated version." ) );
        }

        // A non-CI build produces a "local/" version: it lives in the developer's own feed, unpublished.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository 'X-App' has a non published version 'local/v0.0.0'." ) );
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: no published version at all." ) );
        }

        // Building X-Core too gives it a pending local release: two repositories, two different causes, and
        // the final error names both of them.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: no published version at all and pending local releases." ) );
        }

        File.Exists( StackFolder( stack ).AppendPart( "Test@net8.xml" ) )
            .ShouldBeFalse( "The new World has not been created." );
        VersionBounds( LoadWorldDefinition( stack, "Test.xml" ) )
            .ShouldBe( [("X-Core", null, null), ("X-App", null, null)],
                       "The InfVersion set on X-Core before X-App was refused has been discarded." );
    }

    /// <summary>
    /// Every version being published is not enough: the "dev/" root branch of each repository must be
    /// integrated (or carry nothing new). Code that sits there is code the LTS World would inherit without a
    /// version, and that the default World would then have to produce above the cut.
    /// </summary>
    [Test]
    public async Task lts_create_requires_the_dev_branches_to_be_integrated_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.2.3" ).ConfigureAwait( false );
        var rApp = await world.CreateRepoAsync( "X-App", "v0.4.0", references: [rCore] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        // The publication has integrated everything: an LTS can be created at this point.
        // Committing on "dev/stable" without publishing takes that away.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rApp.WorkingFolderPath, branchName: "dev/stable" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository 'X-App' has a 'dev/stable' branch with non published code in it." ) );
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: non published code in a 'dev/stable' branch." ) );
        }
        File.Exists( StackFolder( stack ).AppendPart( "Test@net8.xml" ) ).ShouldBeFalse();
        VersionBounds( LoadWorldDefinition( stack, "Test.xml" ) )
            .ShouldBe( [("X-Core", null, null), ("X-App", null, null)] );
    }

    /// <summary>
    /// A pending "local/" release is refused even when the repository offers a published version — the case
    /// that neither of the other two checks can see: <c>HotZone.LastStable</c> stays the published version (a
    /// "local/" TagCommit heads LastStables only when it carries a FakeVersion) and a non-CI build integrates
    /// and deletes "dev/stable" as it goes, so the "dev/" check has nothing left to look at.
    /// <para>
    /// It has to be refused: the local version is below the cut, so it would land in the LTS World while the
    /// code it was built from continues in the default one — the developer cannot tell where his pending work
    /// ends up.
    /// </para>
    /// </summary>
    [Test]
    public async Task lts_create_refuses_a_pending_local_release_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.2.3" ).ConfigureAwait( false );
        var rApp = await world.CreateRepoAsync( "X-App", "v0.4.0", references: [rCore] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        // A non-CI build: X-Core gets a "local/v1.2.5" that only exists in this developer's own feed.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "dev/stable" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core v1.2.4 → ⏚/v1.2.5 (CodeChange)   
            2 -  X-App  v0.4.1 → ⏚/v0.4.2 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Both repositories have been built, so both have a pending local release.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "2 repositories have pending local releases:" )
                                     && l.Contains( "'X-Core': local/v1.2.5" )
                                     && l.Contains( "'X-App': local/v0.4.2" ) );
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: pending local releases." ) );
        }
        File.Exists( StackFolder( stack ).AppendPart( "Test@net8.xml" ) ).ShouldBeFalse();
        VersionBounds( LoadWorldDefinition( stack, "Test.xml" ) )
            .ShouldBe( [("X-Core", null, null), ("X-App", null, null)] );

        // Publishing them resolves it: the local versions become the published ones.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeTrue();
        VersionBounds( LoadWorldDefinition( stack, "Test@net8.xml" ) )
            .ShouldBe( [("X-Core", null, "2.0.0-0"), ("X-App", null, "1.0.0-0")] );
    }

    /// <summary>
    /// The Published folder is World scoped: ".PublicStack/Published" for the default World and
    /// ".PublicStack/{LTSName}/Published" for a LTS one - the LocalWorldName.SharedDataFolder convention that
    /// the plugin solution and the CommonFiles folder already follow.
    /// <para>
    /// PublishPlugin used to compose it from the StackRepository.StackWorkingFolder instead, so every World
    /// of a Stack shared one folder: an LTS World's profiles landed in the default World's, in its index, and
    /// competed for the next free Patch of the day.
    /// </para>
    /// </summary>
    [Test]
    public async Task the_Published_folder_is_World_scoped_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.2.3" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-App", "v0.4.0", references: [rCore] ).ConfigureAwait( false );

        // An LTS World can only be created from a World whose "dev/" branches are integrated: only a
        // publication does that. It also writes the default World's first profile.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var defaultPublished = StackFolder( stack ).AppendPart( "Published" );
        new PublishedFolder( defaultPublished ).Profiles.Count().ShouldBe( 1 );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "create", "@net8" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "lts", "clone", "@net8" )).ShouldBeTrue();

        // Opening the LTS World and asking its PublishPlugin where it publishes is enough: the folder is
        // created on demand, so its existence is the assertion. Publishing there would need its brand new
        // "@net8/stable" branch to be opened in the repositories first.
        var ltsEnv = world.WorldRoot.ChangeDirectory( stack.StackRoot.AppendPart( "@net8" ) );
        var (ltsStack, ltsWorld) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, ltsEnv, out var error, skipPullStack: true );
        error.ShouldBeFalse();
        using( ltsStack )
        {
            var publish = ltsWorld.ShouldNotBeNull().GetRequiredPlugin<PublishPlugin>( TestHelper.Monitor ).ShouldNotBeNull();
            var ltsPublished = StackFolder( stack ).AppendPart( "@net8" ).AppendPart( "Published" );
            publish.PublishedFolder.RootPath.ShouldBe( Path.GetFullPath( ltsPublished ) + Path.DirectorySeparatorChar );
            Directory.Exists( ltsPublished ).ShouldBeTrue( "Created on demand." );
            new PublishedFolder( ltsPublished ).Profiles.ShouldBeEmpty( "The LTS World has published nothing." );
        }
        // And the default World's folder is untouched: the two are independent.
        new PublishedFolder( defaultPublished ).Profiles.Count().ShouldBe( 1 );
    }

    static NormalizedPath StackFolder( FakeBuildStack stack ) => stack.StackRoot.AppendPart( ".PublicStack" );

    static XElement LoadWorldDefinition( FakeBuildStack stack, string fileName )
    {
        return XDocument.Load( StackFolder( stack ).AppendPart( fileName ) ).Root!;
    }

    /// <summary>
    /// Projects the VersionTag configuration of every repository of a World definition.
    /// </summary>
    static (string Url, string? InfVersion, string? SupVersion)[] VersionBounds( XElement worldRoot )
    {
        return worldRoot.Descendants( "Repository" )
                        .Select( r =>
                        {
                            var v = r.Element( "VersionTag" );
                            return (r.Attribute( "Url" )!.Value,
                                    v?.Attribute( "InfVersion" )?.Value,
                                    v?.Attribute( "SupVersion" )?.Value);
                        } )
                        .ToArray();
    }
}
