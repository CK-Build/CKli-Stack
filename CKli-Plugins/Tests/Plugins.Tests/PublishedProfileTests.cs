using CK.Core;
using CK.Packaging.Abstractions;
using CKli;
using CKli.Core;
using CKli.Publish.Plugin;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A successful publication leaves the <see cref="PublishedProfile"/> it offers in the Stack's
/// "Published" folder. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class PublishedProfileTests
{
    /// <summary>
    /// The profile describes what the World offers: one <see cref="Repository"/> per Repo, each with the
    /// packages it produced. Its own version is minted from the day of the publication, it is NOT one of
    /// the package versions.
    /// </summary>
    [Test]
    public async Task a_publication_writes_the_profile_it_offers_Async()
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
        // the offer of that package is X-Core's.
        p.Repositories[0].Packages.Select( x => x.ToString() ).ShouldBe( ["X.Consumer@0.3.4"] );
        p.Repositories[1].Packages.Select( x => x.ToString() ).ShouldBe( ["X.Core@1.0.2"] );
        p.Packages.Count.ShouldBe( 2 );
        p.Packages["X.Core"].Version.ToString().ShouldBe( "1.0.2" );
        p.Packages["x.consumer"].Version.ToString().ShouldBe( "0.3.4" );
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
    /// Deprecating a version deprecates every published profile that offers it. The propagation across the
    /// consumers applies: only the profiles that offer one of the deprecated versions are impacted, so a
    /// later profile that offers the same packages in newer versions is left alone.
    /// </summary>
    [Test]
    public async Task deprecating_a_version_deprecates_the_profiles_that_offer_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Consumer", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // The first publication offers X.Core@1.0.2 and X.Consumer@0.3.4.
        await TouchDevStableAsync( rCore, "First.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        // The second one offers X.Core@1.0.3 and X.Consumer@0.3.5.
        await TouchDevStableAsync( rCore, "Second.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();

        var folder = new PublishedFolder( stack.StackRoot.AppendPart( StackRepository.PublicStackName )
                                                         .AppendPart( "Published" ) );
        // Profiles are ordered by descending version: the newer publication comes first.
        folder.Profiles.Select( p => p.Packages["X.Core"].Version.ToString() ).ShouldBe( ["1.0.3", "1.0.2"] );
        folder.Profiles.Any( p => p.IsDeprecated ).ShouldBeFalse();

        // Deprecating X.Core v1.0.2 hits the first profile, which offers it. The second one offers only
        // newer versions and must be left alone. (The deprecation also propagates to X-Consumer v0.3.4,
        // but that package lives in the same profile, so it is not what this assertion discriminates.)
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.2", "--days", "30" )).ShouldBeTrue();

        folder.Reload();
        folder.LoadErrors.ShouldBeEmpty();
        folder.Profiles.Select( p => p.IsDeprecated ).ShouldBe( [false, true] );

        // It is idempotent: deprecating it again changes nothing.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "version", "deprecate", "v1.0.2", "--immediate", "--allow-update" )).ShouldBeTrue();
        folder.Reload();
        folder.Profiles.Select( p => p.IsDeprecated ).ShouldBe( [false, true] );
    }

    // The harness commits on "dev/stable": a successful non-CI publication integrates it into "stable"
    // and deletes it, so it may have to be created again.
    static async Task TouchDevStableAsync( FakeBuildRepo repo, string fileName = "CKliTouchAndCommit.txt" )
    {
        // "git branch dev/stable" fails when it already exists: the error is ignored on purpose.
        await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "exec", "git", "branch", "dev/stable" );
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "dev/stable", fileName: fileName );
    }
}
