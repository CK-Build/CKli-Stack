using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A publication that fails after the version tag has been created leaves the repository with BOTH the
/// "local/vX" and the published "vX" on the same commit: <c>BasePublisher.UnpublishTag</c> recreates the
/// "local/" one and the promoted "vX" must be removed (locally and on the remote).
/// <para>
/// These tests cover the reading side: whatever the compensation managed to do, the VersionTagPlugin must
/// make sense of the resulting tags. Only the fake build harness is used
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </para>
/// </summary>
public class InterruptedPublicationTests
{
    /// <summary>
    /// "local/vX" and "vX" on the same commit, with no other stable version: the published "vX" must be
    /// the lastStable and the "local/" one must become a removable tag.
    /// <para>
    /// Regression: the published tag takes over the "by version" entry of the "local/" one, but topHot and
    /// lastStable were only TRANSFERRED by identity. A "local/" TagCommit is not eligible as lastStable
    /// (it has no FakeVersion here), so lastStable was still null when the published tag replaced it and
    /// never became the published one: "No initial version found in 'X-Core'." on a repository whose only
    /// version IS published.
    /// </para>
    /// </summary>
    [Test]
    public async Task published_version_takes_over_the_local_one_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v0.0.0" ).ConfigureAwait( false );

        // Reproduces what the failed publication left behind: the "local/v0.0.0" that the promotion had
        // removed is back, next to the "v0.0.0" that the compensation failed to delete. Same commit, same
        // annotation: only the "local/" prefix differs.
        using( var e = rCore.CreateEditor() )
        {
            var tags = e.GitRepository.Repository.Tags;
            var published = tags["v0.0.0"];
            published.ShouldNotBeNull( "The initial version is published." );
            tags.Add( "local/v0.0.0",
                      published.Target.Sha,
                      published.Annotation.Tagger,
                      published.Annotation.Message,
                      allowOverwrite: false );
        }
        ReadTags( rCore ).ShouldBe( "ckli-repo, local/v0.0.0, v0.0.0" );

        // Without the fix this warns "No initial version found in 'X-Core'." and the null HotZone makes
        // the command fail on "Please fix any issue before continuing".
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
            logs.ShouldNotContain( "No initial version found in 'X-Core'." );
        }

        // The published tag won: the "local/" duplicate is reported as removable and 'issue --fix' drops it.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue", "--fix" )).ShouldBeTrue();
        ReadTags( rCore ).ShouldBe( "ckli-repo, v0.0.0" );
    }

    static string ReadTags( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                    .Select( t => t.FriendlyName )
                    .Order( System.StringComparer.Ordinal )
                    .Concatenate();
    }
}
