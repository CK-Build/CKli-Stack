using CK.Core;
using CK.Packaging.Abstractions;
using CKli;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.Publish.Plugin;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A successful publication leaves the <see cref="PublishedProfile"/> it carries in the Stack's
/// "Published" folder. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class PublishedProfileTests
{
    /// <summary>
    /// The profile describes what the World produces: one <see cref="Repository"/> per Repo, each with the
    /// packages it produced. Its own version is minted from the day of the publication, it is NOT one of
    /// the package versions.
    /// </summary>
    [Test]
    public async Task a_publication_writes_its_profile_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        var publishedPath = stack.StackRoot.AppendPart( StackRepository.PublicStackName ).AppendPart( "Published" );
        Directory.Exists( publishedPath ).ShouldBeFalse( "Nothing has been published yet." );

        // New code in X-Core: publishing builds and publishes its v1.0.2 and the consumer's v0.3.4.
        await TouchDevStableAsync( rCore ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var today = DateTime.UtcNow;
        var folder = new PublishedFolder( publishedPath );
        var profiles = folder.Profiles.ToList();
        folder.LoadErrors.ShouldBeEmpty();
        profiles.Count.ShouldBe( 1 );
        var p = profiles[0];

        // The profile's version is the publication's own: Major is the year, Minor the day in the year and
        // the Patch starts at 0. The branch that has been published is the root one: a Stable, non CI version.
        p.Version.Major.ShouldBe( today.Year );
        p.Version.Minor.ShouldBe( today.DayOfYear );
        p.Version.Patch.ShouldBe( 0 );
        p.Version.VersionKind.ShouldBe( CSVersionKind.Stable );
        p.Version.IsCI.ShouldBeFalse();
        File.Exists( folder.GetProfileFilePath( p.Version ) ).ShouldBeTrue();

        // The publication also refreshed the index at the folder's root: it reflects the profile files,
        // and a stable publication lands in the always-present "(stable)" group.
        File.ReadAllText( folder.IndexFilePath ).ShouldBe( $$"""
            {
              "Alive": {
                "(stable)": [
                  "{{p.Version}}"
                ]
              },
              "Deprecated": {
                "(stable)": []
              }
            }
            """ );

        p.World.FullName.ShouldBe( "Test" );
        p.StackUrl.AbsoluteUri.ShouldEndWith( "Test-Stack" );
        p.IsDeprecated.ShouldBeFalse();

        // A Repository is identified by its Repo's origin url and its (never null) CKliRepoId. They are
        // ordered by url: "X-Consumer" comes before "X-Core".
        p.Repositories.Length.ShouldBe( 2 );
        p.Repositories[0].Key.Url.AbsoluteUri.ShouldEndWith( "X-Consumer" );
        p.Repositories[1].Key.Url.AbsoluteUri.ShouldEndWith( "X-Core" );
        p.Repositories[0].Key.Id.IsValid.ShouldBeTrue();
        p.Repositories[1].Key.Id.IsValid.ShouldBeTrue();
        p.Repositories[0].Key.Id.ShouldNotBe( p.Repositories[1].Key.Id );

        // A package belongs to the repository that produces it: X-Consumer consumes X.Core@1.0.2 but
        // that package is produced by X-Core.
        p.Repositories[0].Packages.Select( x => x.ToString() ).ShouldBe( ["X.Consumer@0.3.4"] );
        p.Repositories[1].Packages.Select( x => x.ToString() ).ShouldBe( ["X.Core@1.0.2"] );
        p.ProducedPackages.Count.ShouldBe( 2 );
        p.ProducedPackages["X.Core"].Version.ToString().ShouldBe( "1.0.2" );
        p.ProducedPackages["x.consumer"].Version.ToString().ShouldBe( "0.3.4" );
    }

    /// <summary>
    /// Beyond what its repositories produce, a profile records what they consume from OUTSIDE the World: the
    /// DirectDependencies. A package the World produces is never one of them - it belongs to the repository
    /// that produces it - and an identifier consumed by two repositories appears once.
    /// </summary>
    [Test]
    public async Task a_publication_records_the_external_packages_it_is_built_against_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // "Ext.Shared" is referenced by both repositories in the same version, "Ext.OnlyCore" by X-Core alone.
        // Adding these references is also what makes both repositories build.
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( "X.Core", "Ext.Shared", SVersion.Parse( "3.1.0" ), "dev/stable" );
            e.AddOrUpdateReference( "X.Core", "Ext.OnlyCore", SVersion.Parse( "1.2.3" ), "dev/stable" );
        }
        using( var e = rConsumer.CreateEditor() )
        {
            e.AddOrUpdateReference( "X.Consumer", "Ext.Shared", SVersion.Parse( "3.1.0" ), "dev/stable" );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        folder.LoadErrors.ShouldBeEmpty();
        var p = folder.Profiles.Single();

        // X.Core is consumed by X-Consumer but produced by this World: it is a produced package, not a
        // direct dependency. The direct dependencies are sorted by identifier.
        p.ProducedPackages.Keys.Order().ShouldBe( ["X.Consumer", "X.Core"] );
        p.DirectDependencies.Select( x => x.ToString() ).ShouldBe( ["Ext.OnlyCore@1.2.3", "Ext.Shared@3.1.0"] );

        // The direct dependencies survive the Json round trip.
        folder.Reload();
        folder.Profiles.Single().DirectDependencies.Select( x => x.ToString() )
              .ShouldBe( ["Ext.OnlyCore@1.2.3", "Ext.Shared@3.1.0"] );
    }

    /// <summary>
    /// Two publications of the same day on the same branch: the second one increments the Patch. A CI
    /// publication is a CI version, so it never collides with the non CI one.
    /// </summary>
    [Test]
    public async Task the_profiles_of_a_day_are_distinguished_by_their_patch_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ),
                                          createIfMissing: true );
        var today = DateTime.UtcNow;
        var expected = $"{today.Year}.{today.DayOfYear}";

        await TouchDevStableAsync( rCore, "First.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        // A CI publication: the profile is a CI version at the same Patch. Its file is in the folder's root
        // like any stable one (SVersion.BranchName is the empty string for a stable version AND its CI builds).
        await TouchDevStableAsync( rCore, "Second.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--ci" )).ShouldBeTrue();

        await TouchDevStableAsync( rCore, "Third.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        folder.Reload();
        folder.LoadErrors.ShouldBeEmpty();
        // Profiles are ordered by descending version: "X.Y.1" then "X.Y.0" then its CI build.
        folder.Profiles.Select( x => x.Version.ToString() )
              .ShouldBe( [$"{expected}.1", $"{expected}.0", $"{expected}.0--ci.0"] );
    }

    /// <summary>
    /// Deprecating a version deprecates every published profile that carries it. The propagation across the
    /// consumers applies: only the profiles that carry one of the deprecated versions are impacted, so a
    /// later profile that carries the same packages in newer versions is left alone.
    /// </summary>
    [Test]
    public async Task deprecating_a_version_deprecates_the_profiles_that_carry_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // The first publication carries X.Core@1.0.2 and X.Consumer@0.3.4.
        await TouchDevStableAsync( rCore, "First.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        // The second one carries X.Core@1.0.3 and X.Consumer@0.3.5.
        await TouchDevStableAsync( rCore, "Second.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        // Profiles are ordered by descending version: the newer publication comes first.
        folder.Profiles.Select( p => p.ProducedPackages["X.Core"].Version.ToString() ).ShouldBe( ["1.0.3", "1.0.2"] );
        folder.Profiles.Any( p => p.IsDeprecated ).ShouldBeFalse();

        // Deprecating X.Core v1.0.2 hits the first profile, which carries it. The second one carries only
        // newer versions and must be left alone. (The deprecation also propagates to X-Consumer v0.3.4,
        // but that package lives in the same profile, so it is not what this assertion discriminates.)
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.2", "--days", "30" )).ShouldBeTrue();

        folder.Reload();
        folder.LoadErrors.ShouldBeEmpty();
        folder.Profiles.Select( p => p.IsDeprecated ).ShouldBe( [false, true] );
        // Re-running the same deprecation changes nothing: the deprecation of a profile is monotonic.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.2", "--days", "30", "--allow-update", "--reason", "Again." )).ShouldBeTrue();
        folder.Reload();
        folder.Profiles.Select( p => p.IsDeprecated ).ShouldBe( [false, true] );

        // Expiring it ("--immediate") removes the version tag and unlists the packages: the profile that
        // carries them no longer describes anything, so it is deleted rather than kept as deprecated.
        var removed = folder.Profiles.Single( p => p.IsDeprecated ).Version;
        var removedFile = folder.GetProfileFilePath( removed );
        File.Exists( removedFile ).ShouldBeTrue();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.2", "--immediate", "--allow-update" )).ShouldBeTrue();

        folder.Reload();
        folder.LoadErrors.ShouldBeEmpty();
        folder.Profiles.Select( p => p.ProducedPackages["X.Core"].Version.ToString() ).ShouldBe( ["1.0.3"] );
        File.Exists( removedFile ).ShouldBeFalse();

        // And that is idempotent too: there is nothing left to remove.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.2", "--immediate", "--allow-update" )).ShouldBeTrue();
        folder.Reload();
        folder.Profiles.Select( p => p.ProducedPackages["X.Core"].Version.ToString() ).ShouldBe( ["1.0.3"] );
    }

    /// <summary>
    /// A fix publishes versions that supersede the ones it fixes. The profiles that carry those are not
    /// rewritten - they record what was published - so the fix adds a superseding profile beside each one,
    /// with the same Major.Minor and the next free Patch. A profile that carries only newer versions is left
    /// alone.
    /// </summary>
    [Test]
    public async Task a_fix_publication_supersedes_the_profiles_that_carry_the_fixed_versions_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Consumer", "v0.1.0", references: [rCore] ).ConfigureAwait( false );

        // Two publications, each triggered by a "feat:" commit. The first profile carries X.Core@1.1.0 and
        // X.Consumer@0.2.0, the second one X.Core@1.2.0 and X.Consumer@0.3.0. The second publication is also
        // what pushes v1.1 out of the hot zone so that it can be fixed.
        await TouchDevStableAsync( rCore, "First.txt", "feat: a first feature." ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        await TouchDevStableAsync( rCore, "Second.txt", "feat: a second feature." ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        var today = DateTime.UtcNow;
        var day = $"{today.Year}.{today.DayOfYear}";
        folder.Profiles.Select( p => p.ProducedPackages["X.Core"].Version.ToString() ).ShouldBe( ["1.2.0", "1.1.0"] );

        // Fixing v1.1 publishes X.Core@1.1.1 and, since X-Consumer v0.2.0 consumed X.Core@1.1.0, X.Consumer@0.2.1.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "start", "v1.1" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "fix/v1.1", fileName: "The-fix.txt" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "publish" )).ShouldBeTrue();

        folder.Reload();
        folder.LoadErrors.ShouldBeEmpty();
        // A third profile: the one that carried the fixed versions has been superseded. Both were published
        // today, so the successor takes the next free Patch.
        folder.Profiles.Select( p => p.Version.ToString() )
              .ShouldBe( [$"{day}.2", $"{day}.1", $"{day}.0"] );

        var superseding = folder.Find( SVersion.Parse( $"{day}.2" ) )!;
        superseding.ProducedPackages["X.Core"].Version.ToString().ShouldBe( "1.1.1" );
        superseding.ProducedPackages["X.Consumer"].Version.ToString().ShouldBe( "0.2.1" );
        superseding.IsDeprecated.ShouldBeFalse();

        // The profile it supersedes is untouched, and the one that carries only newer versions is not concerned.
        var superseded = folder.Find( SVersion.Parse( $"{day}.0" ) )!;
        superseded.ProducedPackages["X.Core"].Version.ToString().ShouldBe( "1.1.0" );
        superseded.ProducedPackages["X.Consumer"].Version.ToString().ShouldBe( "0.2.0" );
        var untouched = folder.Find( SVersion.Parse( $"{day}.1" ) )!;
        untouched.ProducedPackages["X.Core"].Version.ToString().ShouldBe( "1.2.0" );
    }

    /// <summary>
    /// The fake build restores nothing, so a transitive set exists in this harness only when a test declares
    /// one on <see cref="FakeBuildRepo.TransitivePackages"/>. That declaration reaches the version tag of every
    /// build of the repository - the initial one included, which is what a publication that does NOT rebuild it
    /// reads - and a repository that declares nothing keeps an unrecorded set.
    /// </summary>
    [Test]
    public async Task the_fake_harness_records_the_transitive_packages_a_test_declares_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // Nothing has been declared yet: the initial version tags carry no transitive section at all.
        ReadContent( rCore, "v1.0.1" ).HasTransitive.ShouldBeFalse();

        // Declaring rewrites the initial version tag in place - same commit, so no "--ci.N" moves.
        // The declaration is a set: it is sorted here, not by the test.
        rCore.TransitivePackages = [Instance( "Ext.Deeper@1.5.0" ), Instance( "Ext.Deep@2.0.0" )];
        var initial = ReadContent( rCore, "v1.0.1" );
        initial.HasTransitive.ShouldBeTrue();
        initial.Transitive.Select( p => p.ToString() ).ShouldBe( ["Ext.Deep@2.0.0", "Ext.Deeper@1.5.0"] );

        // An empty declaration is NOT the absence of one: it states that a restore brings nothing.
        rConsumer.TransitivePackages = [];
        var consumerInitial = ReadContent( rConsumer, "v0.3.3" );
        consumerInitial.HasTransitive.ShouldBeTrue();
        consumerInitial.Transitive.ShouldBeEmpty();

        // The declaration feeds every subsequent fake build: publishing X-Core rebuilds it and its consumer.
        await TouchDevStableAsync( rCore ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        ReadContent( rCore, "v1.0.2" ).Transitive.Select( p => p.ToString() )
                                      .ShouldBe( ["Ext.Deep@2.0.0", "Ext.Deeper@1.5.0"] );
        var rebuiltConsumer = ReadContent( rConsumer, "v0.3.4" );
        rebuiltConsumer.HasTransitive.ShouldBeTrue();
        rebuiltConsumer.Transitive.ShouldBeEmpty();

        static BuildContentInfo ReadContent( FakeBuildRepo repo, string tagName )
        {
            using var e = repo.CreateEditor();
            var tag = e.GitRepository.Repository.Tags[tagName];
            tag.ShouldNotBeNull( $"Tag '{tagName}' not found in '{repo.DisplayPath}'." );
            BuildContentInfo.TryParse( tag.Annotation.Message, out var content ).ShouldBeTrue();
            return content!;
        }
    }

    /// <summary>
    /// Beyond the packages its repositories reference, a profile records what a restore brings: the union of
    /// what NuGet resolved transitively for each of them. An identifier the profile already states - a direct
    /// dependency here - is not repeated by it.
    /// </summary>
    [Test]
    public async Task a_publication_records_what_a_restore_brings_beyond_its_direct_dependencies_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // Both repositories reference "Ext.Shared" - that is what makes them build, and what puts the
        // identifier in the direct dependencies.
        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( "X.Core", "Ext.Shared", SVersion.Parse( "3.1.0" ), "dev/stable" );
        }
        using( var e = rConsumer.CreateEditor() )
        {
            e.AddOrUpdateReference( "X.Consumer", "Ext.Shared", SVersion.Parse( "3.1.0" ), "dev/stable" );
        }
        // A restore of either brings "Ext.Deep", which nobody references. X-Consumer's restore also brings
        // the very "Ext.Shared" it references: that one is already stated by the direct dependencies.
        rCore.TransitivePackages = [Instance( "Ext.Deep@2.0.0" )];
        rConsumer.TransitivePackages = [Instance( "Ext.Deep@2.0.0" ), Instance( "Ext.Shared@3.1.0" )];

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        folder.LoadErrors.ShouldBeEmpty();
        var p = folder.Profiles.Single();

        p.DirectDependencies.Select( x => x.ToString() ).ShouldBe( ["Ext.Shared@3.1.0"] );
        p.TransitiveDependencies.Regular.Select( x => x.ToString() ).ShouldBe( ["Ext.Deep@2.0.0"] );
        p.TransitiveDependencies.Ambiguous.ShouldBeEmpty( "Ext.Shared is a direct dependency at the very "
                                                          + "version the restore brings: nothing to report." );

        // The transitive dependencies survive the Json round trip.
        folder.Reload();
        folder.Profiles.Single().TransitiveDependencies.Regular.Select( x => x.ToString() )
              .ShouldBe( ["Ext.Deep@2.0.0"] );
    }

    /// <summary>
    /// Two repositories can resolve one identifier to two versions - their graphs differ, and the 'D' mapping
    /// aligns the DIRECT references only. Such a disagreement is recorded with the repositories that made it,
    /// resolved from the transitive packages themselves when nothing anchors the identifier and anchored on
    /// the profile's own entry when something does.
    /// </summary>
    [Test]
    public async Task transitive_packages_that_disagree_are_recorded_with_the_repositories_that_resolved_them_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( "X.Core", "Ext.Shared", SVersion.Parse( "3.1.0" ), "dev/stable" );
        }
        using( var e = rConsumer.CreateEditor() )
        {
            e.AddOrUpdateReference( "X.Consumer", "Ext.Shared", SVersion.Parse( "3.1.0" ), "dev/stable" );
        }
        // "Ext.Deep" is nobody's reference and the two restores disagree on it. "Ext.Shared" IS referenced,
        // in 3.1.0, but X-Consumer's restore resolved 4.0.0: that is the harmful direction.
        rCore.TransitivePackages = [Instance( "Ext.Deep@1.0.0" )];
        rConsumer.TransitivePackages = [Instance( "Ext.Deep@2.0.0" ), Instance( "Ext.Shared@4.0.0" )];

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
            // These are external packages nobody here references: reported, never gated.
            logs.ShouldContain( "2 transitive package(s) resolved to more than one version across this "
                                + "publication, or to a version this publication does not carry: "
                                + "Ext.Deep, Ext.Shared." );
        }

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        folder.LoadErrors.ShouldBeEmpty();
        var p = folder.Profiles.Single();
        var coreId = p.Repositories.Single( r => r.Key.Url.AbsoluteUri.EndsWith( "X-Core" ) ).Key.Id;
        var consumerId = p.Repositories.Single( r => r.Key.Url.AbsoluteUri.EndsWith( "X-Consumer" ) ).Key.Id;

        var t = p.TransitiveDependencies;
        t.Regular.ShouldBeEmpty();
        t.Ambiguous.Select( a => $"{a} ({a.ResolvedFrom})" )
                   .ShouldBe( ["Ext.Deep@2.0.0 (TransitiveDependencies)",
                               "Ext.Shared@3.1.0 (DirectDependencies)"] );

        // Nothing anchors Ext.Deep: the version is the greatest resolution (NuGet's highest-wins) and the
        // resolutions are sorted by descending version.
        var deep = t.Ambiguous[0];
        deep.Resolutions.Select( r => r.Version.ToString() ).ShouldBe( ["2.0.0", "1.0.0"] );
        deep.Resolutions[0].Repositories.ShouldBe( [consumerId] );
        deep.Resolutions[1].Repositories.ShouldBe( [coreId] );

        // Ext.Shared is anchored on the direct dependency: only the greater resolution is reported.
        var shared = t.Ambiguous[1];
        shared.Resolutions.Select( r => r.Version.ToString() ).ShouldBe( ["4.0.0"] );
        shared.Resolutions[0].Repositories.ShouldBe( [consumerId] );

        // And all of it survives the Json round trip, repository identifiers included.
        folder.Reload();
        var back = folder.Profiles.Single().TransitiveDependencies;
        back.Ambiguous.Select( a => $"{a} ({a.ResolvedFrom})" ).ShouldBe( t.Ambiguous.Select( a => $"{a} ({a.ResolvedFrom})" ) );
        back.Ambiguous[0].Resolutions.ShouldBe( deep.Resolutions );
    }

    /// <summary>
    /// A fix successor is a projection of its origin: it carries the dependencies verbatim, except that an
    /// ambiguity anchored on a produced package the fix moves is re-anchored on the superseding version -
    /// and disappears when that version has caught up with everything it reported.
    /// </summary>
    [Test]
    public async Task a_fix_successor_carries_the_dependencies_and_re_anchors_its_ambiguities_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.0", references: [rCore] ).ConfigureAwait( false );

        using( var e = rCore.CreateEditor() )
        {
            e.AddOrUpdateReference( "X.Core", "Ext.Shared", SVersion.Parse( "3.1.0" ), "dev/stable" );
        }
        // X-Consumer's restore claims two X.Core the first publication does not carry: the one the fix will
        // produce (1.1.1) and one no publication ever will (9.9.9). Both are ambiguities anchored on the
        // produced X.Core@1.1.0; after the fix only 9.9.9 still is.
        rConsumer.TransitivePackages = [Instance( "X.Core@1.1.1" ), Instance( "X.Core@9.9.9" )];

        // Two publications: X.Core@1.1.0 then X.Core@1.2.0. The second one is also what pushes v1.1 out of
        // the hot zone so that it can be fixed.
        await TouchDevStableAsync( rCore, "First.txt", "feat: a first feature." ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        await TouchDevStableAsync( rCore, "Second.txt", "feat: a second feature." ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        var today = DateTime.UtcNow;
        var day = $"{today.Year}.{today.DayOfYear}";

        var origin = folder.Find( SVersion.Parse( $"{day}.0" ) )!;
        origin.ProducedPackages["X.Core"].Version.ToString().ShouldBe( "1.1.0" );
        origin.TransitiveDependencies.Ambiguous.Single()
              .Resolutions.Select( r => r.Version.ToString() ).ShouldBe( ["9.9.9", "1.1.1"] );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "start", "v1.1" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "fix/v1.1", fileName: "The-fix.txt" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "publish" )).ShouldBeTrue();

        folder.Reload();
        folder.LoadErrors.ShouldBeEmpty();
        var superseding = folder.Find( SVersion.Parse( $"{day}.2" ) )!;
        superseding.ProducedPackages["X.Core"].Version.ToString().ShouldBe( "1.1.1" );

        // The direct dependencies are carried verbatim.
        superseding.DirectDependencies.Select( x => x.ToString() ).ShouldBe( ["Ext.Shared@3.1.0"] );

        // The ambiguity moved with its anchor, and 1.1.1 is no longer a disagreement.
        var a = superseding.TransitiveDependencies.Ambiguous.Single();
        a.PackageId.ShouldBe( "X.Core" );
        a.Version.ToString().ShouldBe( "1.1.1" );
        a.ResolvedFrom.ShouldBe( VersionSource.ProducedPackages );
        a.Resolutions.Select( r => r.Version.ToString() ).ShouldBe( ["9.9.9"] );

        // A profile records what was published: the origin is untouched.
        folder.Find( SVersion.Parse( $"{day}.0" ) )!.TransitiveDependencies.Ambiguous.Single()
              .Resolutions.Select( r => r.Version.ToString() ).ShouldBe( ["9.9.9", "1.1.1"] );
    }

    static PackageInstance Instance( string s )
    {
        var i = s.IndexOf( '@' );
        return new PackageInstance( s[..i], SVersion.Parse( s[(i + 1)..] ) );
    }

    // The harness commits on "dev/stable": a successful non-CI publication integrates it into "stable"
    // and deletes it, so it may have to be created again.
    static async Task TouchDevStableAsync( FakeBuildRepo repo,
                                           string fileName = "CKliTouchAndCommit.txt",
                                           string? commitMessage = null )
    {
        // "git branch dev/stable" fails when it already exists: the error is ignored on purpose.
        await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "exec", "git", "branch", "dev/stable" );
        TestHelper.TouchAndCommit( repo.WorkingFolderPath,
                                   branchName: "dev/stable",
                                   commitMessage: commitMessage,
                                   fileName: fileName );
    }
}
