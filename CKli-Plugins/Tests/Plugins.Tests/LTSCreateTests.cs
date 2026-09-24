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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core v1.2.3 → ⏚/v1.2.4 (CodeChange)   
            2 -  X-App  v0.4.0 → ⏚/v0.4.1 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeTrue();

        // The new World's definition file is a "{StackName}{LTSName}.xml" file in its "{LTSName}/" folder of the Stack. Its root
        // element keeps the Stack name ('@' is not a valid XML name character) and carries the LTSName.
        var ltsRoot = LoadWorldDefinition( stack, "@net8/Test@net8.xml" );
        ltsRoot.Name.LocalName.ShouldBe( "Test" );
        ltsRoot.Attribute( "LTSName" )!.Value.ShouldBe( "@net8" );

        // The LTS World is bounded from above and keeps its (here absent) InfVersion.
        VersionBounds( ltsRoot ).ShouldBe( [("X-Core", null, "2.0.0-0"), ("X-App", null, "1.0.0-0")] );

        // The default World is bounded from below by the very same cut: the versions it has produced so far
        // now belong to the LTS World only.
        VersionBounds( LoadWorldDefinition( stack, "Test.xml" ) )
            .ShouldBe( [("X-Core", "2.0.0-0", null), ("X-App", "1.0.0-0", null)] );

        // The cut is usable: the default World has no version left, and the command has given each repository
        // its initial version exactly AT the cut (the "-0" prerelease is dropped) - this is what an InfVersion
        // set on the cut's own Major.Minor.Patch would forbid. The "+fake" is on a new empty commit of the root
        // branch: the tip carried the last published version, which now belongs to the LTS World. Both are pushed.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );
        foreach( var (name, published, fake) in new[] { ("X-Core", "v1.2.4", "v2.0.0+fake"), ("X-App", "v0.4.1", "v1.0.0+fake") } )
        {
            using var git = new LibGit2Sharp.Repository( stack.StackRoot.AppendPart( name ) );
            var fakeCommit = (LibGit2Sharp.Commit)git.Tags[fake].ShouldNotBeNull().PeeledTarget;
            fakeCommit.Parents.Single().ShouldBe( (LibGit2Sharp.Commit)git.Tags[published].ShouldNotBeNull().PeeledTarget );
            git.Branches["stable"].Tip.ShouldBe( fakeCommit );
            git.Branches["origin/stable"].Tip.ShouldBe( fakeCommit, "The new commit is pushed." );
            using var bare = new LibGit2Sharp.Repository( stack.Remotes.GetUriFor( name ).LocalPath );
            bare.Tags[fake].ShouldNotBeNull( "The +fake tag is pushed." );
        }

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release", "--dry-run" )).ShouldBeTrue();
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
        ltsBranchModel.Attribute( "Root" )!.Value.ShouldBe( "stable" );
        ltsBranchModel.Elements( "Explo" ).ShouldBeEmpty();

        // The new World has been cloned by the command: its repositories are in the Stack's own "@net8/" folder.
        // Its root branch "@net8/stable" has been created and pushed on the commit of the last published version
        // of each repository - the LTS starts there, the future stays in the default World - so the new World
        // has no issue at all.
        var ltsWorld = world.WorldRoot.ChangeDirectory( stack.StackRoot.AppendPart( "@net8" ) );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, ltsWorld, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );
        foreach( var (name, version) in new[] { ("X-Core", "v1.2.4"), ("X-App", "v0.4.1") } )
        {
            using var lts = new LibGit2Sharp.Repository( stack.StackRoot.Combine( $"@net8/{name}" ) );
            lts.Branches["origin/@net8/stable"].ShouldNotBeNull().Tip
               .ShouldBe( (LibGit2Sharp.Commit)lts.Tags[version].ShouldNotBeNull().PeeledTarget,
                          $"'{name}' LTS starts with its last published version." );
            using var dflt = new LibGit2Sharp.Repository( stack.StackRoot.AppendPart( name ) );
            dflt.Branches["@net8/stable"].ShouldBeNull( "The default World's clone doesn't keep the LTS root branch." );
        }

        // In the LTS World, a breaking change never changes the Major: it is a Minor one (and the SupVersion is
        // respected by construction since the cut is the next Major).
        // The git name of the "dev/" branch ("@net8/dev/stable") is understood by the commands, and no --branch is
        // needed: X-Core is on "@net8/dev/stable" and X-App on "@net8/stable", which is the same branch.
        var ltsCore = stack.StackRoot.Combine( "@net8/X-Core" );
        // A fresh clone is on the remote's default branch: the LTS repositories are first switched to their root.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, ltsWorld, "branch", "switch", "@net8/stable", "--all" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, ltsWorld.ChangeDirectory( ltsCore ), "branch", "switch", "@net8/dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( ltsCore, branchName: "@net8/dev/stable", commitMessage: "feat!: A breaking change." );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, ltsWorld, "build", "--release", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldContain( "X-Core v1.2.4 → ⏚/v1.3.0 (CodeChange)" );
    }

    /// <summary>
    /// The checks are made on the local repositories: a root branch that is not the remote one (here another
    /// developer has published since the last pull) would cut the LTS below that publication. It is refused.
    /// </summary>
    [Test]
    public async Task lts_create_requires_the_root_branches_to_be_the_remote_ones_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.2.3" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // The remote "stable" moves ahead of the local one.
        using( var bare = new LibGit2Sharp.Repository( stack.Remotes.GetUriFor( "X-Core" ).LocalPath ) )
        {
            var tip = bare.Branches["stable"].Tip;
            var sig = new LibGit2Sharp.Signature( "Other", "other@example.com", System.DateTimeOffset.Now );
            var ahead = bare.ObjectDatabase.CreateCommit( sig, sig, "Somebody else's work.", tip.Tree, [tip], prettifyMessage: false );
            bare.Refs.UpdateTarget( bare.Refs["refs/heads/stable"], ahead.Id );
        }

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "The 'stable' branch differs from 'origin/stable' in repository X-Core." ) );
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: 'stable' branches not synchronized with their remote." ) );
        }
        Directory.Exists( StackFolder( stack ).AppendPart( "@net8" ) ).ShouldBeFalse();
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
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository 'X-App' has a current fake or a deprecated version 'v0.0.0+fake'." ) );
            // The final error names the actual cause: it is not a catch-all "the world must be published".
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: a current fake or deprecated version." ) );
        }

        // A non-CI build produces a "local/" version: it lives in the developer's own feed, unpublished.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository 'X-App' has a non published version 'local/v0.0.0'." ) );
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: no published version at all." ) );
        }

        // Building X-Core too gives it a pending local release: two repositories, two different causes, and
        // the final error names both of them.
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: no published version at all and pending local releases." ) );
        }

        File.Exists( StackFolder( stack ).Combine( "@net8/Test@net8.xml" ) )
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // The publication has integrated everything: an LTS can be created at this point.
        // Committing on "dev/stable" without publishing takes that away.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rApp.WorkingFolderPath, branchName: "dev/stable" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Repository has a 'dev/stable' branch (locally or on the remote): X-App." ) );
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: a 'dev/stable' branch." ) );
        }
        File.Exists( StackFolder( stack ).Combine( "@net8/Test@net8.xml" ) ).ShouldBeFalse();
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // A non-CI build: X-Core gets a "local/v1.2.5" that only exists in this developer's own feed.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "dev/stable" );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--release" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  X-Core v1.2.4 → ⏚/v1.2.5 (CodeChange)   
            2 -  X-App  v0.4.1 → ⏚/v0.4.2 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // The default World's "Common/" folder and its "TestRun.Sha" cache (this one is written by a real build,
        // the fake one runs no tests).
        var commonFile = StackFolder( stack ).Combine( "Common/.editorconfig" );
        Directory.CreateDirectory( commonFile.RemoveLastPart() );
        File.WriteAllText( commonFile, "root = true" );
        var testRunSha = StackFolder( stack ).Combine( "$Local/TestRun.Sha.txt" );
        File.WriteAllText( testRunSha, "0123456789abcdef" );

        // Both repositories have been built, so both have a pending local release.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "2 repositories have pending local releases:" )
                                     && l.Contains( "'X-Core': local/v1.2.5" )
                                     && l.Contains( "'X-App': local/v0.4.2" ) );
            logs.ShouldContain( l => l.Contains( "Unable to create a Long Term Support world: pending local releases." ) );
        }
        File.Exists( StackFolder( stack ).Combine( "@net8/Test@net8.xml" ) ).ShouldBeFalse();
        VersionBounds( LoadWorldDefinition( stack, "Test.xml" ) )
            .ShouldBe( [("X-Core", null, null), ("X-App", null, null)] );
        // A refused creation writes nothing: the creation steps have not run.
        Directory.Exists( StackFolder( stack ).AppendPart( "@net8" ) ).ShouldBeFalse();
        Directory.Exists( StackFolder( stack ).Combine( "$Local/@net8" ) ).ShouldBeFalse();
        File.Exists( testRunSha ).ShouldBeTrue( "The TestRun.Sha cache has not been moved." );

        // Publishing them resolves it: the local versions become the published ones.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeTrue();
        VersionBounds( LoadWorldDefinition( stack, "@net8/Test@net8.xml" ) )
            .ShouldBe( [("X-Core", null, "2.0.0-0"), ("X-App", null, "1.0.0-0")] );

        // The CommonFiles plugin has snapshot the "Common/" folder in the new World's one: both now evolve
        // independently.
        File.ReadAllText( StackFolder( stack ).Combine( "@net8/Common/.editorconfig" ) ).ShouldBe( "root = true" );
        File.Exists( commonFile ).ShouldBeTrue( "The default World keeps its own." );
        // The Build plugin has moved the TestRun.Sha cache to the new World's local folder: the LTS World is the
        // one that keeps the code whose tests have run.
        File.ReadAllText( StackFolder( stack ).Combine( "$Local/@net8/TestRun.Sha.txt" ) ).ShouldBe( "0123456789abcdef" );
        File.Exists( testRunSha ).ShouldBeFalse();
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
    /// <para>
    /// The folder is bound to the World: when a LTS World is created, what the default World has published so far
    /// (its versions are below the cut) moves to the LTS World's folder, and the default World starts with an empty
    /// one - with its index - waiting for its first publication.
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        var defaultPublished = StackFolder( stack ).AppendPart( "Published" );
        new PublishedFolder( defaultPublished ).Profiles.Count().ShouldBe( 1 );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "lts", "create", "@net8" )).ShouldBeTrue();

        // Opening the LTS World and asking its PublishPlugin where it publishes: its folder is the moved one.
        var ltsEnv = world.WorldRoot.ChangeDirectory( stack.StackRoot.AppendPart( "@net8" ) );
        var (ltsStack, ltsWorld) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, ltsEnv, out var error, skipPullStack: true );
        error.ShouldBeFalse();
        using( ltsStack )
        {
            var publish = ltsWorld.ShouldNotBeNull().GetRequiredPlugin<PublishPlugin>( TestHelper.Monitor ).ShouldNotBeNull();
            var ltsPublished = StackFolder( stack ).AppendPart( "@net8" ).AppendPart( "Published" );
            publish.PublishedFolder.RootPath.ShouldBe( Path.GetFullPath( ltsPublished ) + Path.DirectorySeparatorChar );
            new PublishedFolder( ltsPublished ).Profiles.Count().ShouldBe( 1, "The default World's profile has moved to the LTS World." );
        }
        // The default World's folder is empty, but it has its index.
        var emptied = new PublishedFolder( defaultPublished );
        emptied.Profiles.ShouldBeEmpty();
        File.Exists( emptied.IndexFilePath ).ShouldBeTrue();
    }

    static NormalizedPath StackFolder( FakeBuildStack stack ) => stack.StackRoot.AppendPart( ".PublicStack" );

    // A LTS World definition file is in the world's own folder: "@net8/Test@net8.xml".
    static XElement LoadWorldDefinition( FakeBuildStack stack, string filePath )
    {
        return XDocument.Load( StackFolder( stack ).Combine( filePath ) ).Root!;
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
