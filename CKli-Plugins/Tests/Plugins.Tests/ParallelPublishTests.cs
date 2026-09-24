using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The repositories of a roadmap are published concurrently (bounded by "--max-dop"), each one once all its
/// upstreams have been published. These tests use the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class ParallelPublishTests
{
    /// <summary>
    /// A diamond: X-Left and X-Right are siblings (both consume X-Core) and X-Top consumes both of them.
    /// Every repository reaches its remote whatever the degree of parallelism.
    /// </summary>
    [TestCase( "1" )]
    [TestCase( "4" )]
    public async Task diamond_is_fully_published_Async( string maxDop )
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var rLeft = await world.CreateRepoAsync( "X-Left", "v1.0.0", references: [rCore] ).ConfigureAwait( false );
        var rRight = await world.CreateRepoAsync( "X-Right", "v1.0.0", references: [rCore] ).ConfigureAwait( false );
        var rTop = await world.CreateRepoAsync( "X-Top", "v1.0.0", references: [rLeft, rRight] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--max-dop", maxDop )).ShouldBeTrue();

        foreach( var name in new[] { "X-Core", "X-Left", "X-Right", "X-Top" } )
        {
            RemoteVersionTags( stack, name ).ShouldNotBeEmpty( $"'{name}' must have been published." );
        }
    }

    /// <summary>
    /// X-Broken's remote already has the version tag that its publication pushes, on another commit: the push is
    /// refused and its publication fails (its packages are pushed, its tag is not). Its consumer X-Top is not even
    /// attempted, while X-Core, their upstream, is published. X-Sibling (a sibling of X-Broken) may or may not have
    /// been published: it runs concurrently and nothing is said about it.
    /// </summary>
    [Test]
    public async Task a_failed_publication_skips_its_consumers_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var rBroken = await world.CreateRepoAsync( "X-Broken", "v1.0.0", references: [rCore] ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Sibling", "v1.0.0", references: [rCore] ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Top", "v1.0.0", references: [rBroken] ).ConfigureAwait( false );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();

        // Breaks the X-Broken publication: its remote "dev/stable" holds a commit that the local one doesn't have,
        // so the branch push (after the tag push) is not a fast-forward and is refused.
        string localTag;
        using( var e = rBroken.CreateEditor() )
        {
            localTag = e.GitRepository.Repository.Tags.Select( t => t.FriendlyName ).Single( n => n.StartsWith( "local/" ) );
        }
        using( var bare = new LibGit2Sharp.Repository( stack.Remotes.GetUriFor( "X-Broken" ).LocalPath ) )
        {
            var tip = bare.Head.Tip;
            var sig = new LibGit2Sharp.Signature( "Other", "other@example.com", System.DateTimeOffset.Now );
            var other = bare.ObjectDatabase.CreateCommit( sig, sig, "Somebody else's work.", tip.Tree, [tip], prettifyMessage: false );
            bare.Refs.Add( "refs/heads/dev/stable", other.Id );
        }

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeFalse();
            logs.ShouldContain( "Unable to publish 'X-Broken'." );
            logs.ShouldContain( "Publication of 'X-Top' skipped: one of its upstream publications failed." );
        }
        RemoteVersionTags( stack, "X-Core" ).ShouldNotBeEmpty();
        RemoteVersionTags( stack, "X-Top" ).ShouldBeEmpty();

        // The X-Broken compensation ran: its pushed version tag is removed from the remote and it is "local/" again.
        RemoteVersionTags( stack, "X-Broken" ).ShouldBeEmpty();
        using( var e = rBroken.CreateEditor() )
        {
            e.GitRepository.Repository.Tags[localTag].ShouldNotBeNull();
        }
    }

    static string[] RemoteVersionTags( FakeBuildStack stack, string repoName )
    {
        using var bare = new LibGit2Sharp.Repository( stack.Remotes.GetUriFor( repoName ).LocalPath );
        return bare.Tags.Select( t => t.FriendlyName ).Where( n => n.StartsWith( 'v' ) && !n.EndsWith( "+fake" ) ).ToArray();
    }
}
