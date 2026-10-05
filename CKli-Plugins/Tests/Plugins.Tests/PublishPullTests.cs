using CK.Core;
using CKli;
using CKli.Core;
using CKli.Publish.Plugin;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A publication starts from the remote state of the World: once its lock is held, "ckli publish" pulls the Stack
/// (where its profile is written) and the repositories. A "ckli build" never touches the remotes.
/// </summary>
public class PublishPullTests
{
    /// <summary>
    /// Bob's Stack is stale when he publishes after Tim, on the same day: without the pull, both profiles would take
    /// the same version (the next free Patch of the day), Bob's file would collide with Tim's and his Stack could
    /// not be pushed.
    /// </summary>
    [Test]
    public async Task a_publication_on_a_stale_Stack_pulls_it_first_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (bob, tim, bobStackPath) = await ArrangeBobAndTimAsync( testEnv ).ConfigureAwait( false );

        await TouchDevStableAsync( tim.ChangeDirectory( "X-Two" ) ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim.ChangeDirectory( "X-Two" ), "publish", "--regular" )).ShouldBeTrue();

        await TouchDevStableAsync( bob.ChangeDirectory( "X-One" ) ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob.ChangeDirectory( "X-One" ), "publish", "--regular" )).ShouldBeTrue();

        var bobProfiles = new PublishedFolder( bobStackPath.AppendPart( "Published" ) ).Profiles.Select( p => p.Version ).ToList();
        bobProfiles.Count.ShouldBe( 3, "The first publication, Tim's and Bob's." );
        bobProfiles.Distinct().Count().ShouldBe( 3 );

        // Bob's Stack has been pushed: Tim gets the 3 profiles.
        using( var git = new LibGit2Sharp.Repository( bobStackPath ) )
        {
            git.Head.TrackingDetails.AheadBy.ShouldBe( 0 );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "pull" )).ShouldBeTrue();
        new PublishedFolder( tim.CurrentStackPath.AppendPart( "Published" ) ).Profiles.Select( p => p.Version )
            .ShouldBe( bobProfiles );
    }

    /// <summary>
    /// The running World has loaded its definition file and its plugins: a remote Stack that changed them cannot be
    /// pulled under it. The publication fails and asks for a "ckli pull" first.
    /// </summary>
    [Test]
    public async Task a_remote_change_of_the_World_definition_stops_the_publication_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (bob, tim, _) = await ArrangeBobAndTimAsync( testEnv ).ConfigureAwait( false );

        var timDefinition = Directory.GetFiles( tim.CurrentStackPath, "*.xml" ).Single();
        File.AppendAllText( timDefinition, "<!-- Tim was here. -->" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "push", "--stack-only" )).ShouldBeTrue();

        await TouchDevStableAsync( bob.ChangeDirectory( "X-One" ) ).ConfigureAwait( false );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, bob.ChangeDirectory( "X-One" ), "publish", "--regular" )).ShouldBeFalse();
            logs.ShouldContain( l => l.StartsWith( "The remote Stack changed what this World has loaded: '" )
                                     && l.Contains( Path.GetFileName( timDefinition ) )
                                     && l.EndsWith( "Run 'ckli pull' and then the command again." ) );
        }
        // Once pulled, the publication works.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob.ChangeDirectory( "X-One" ), "publish", "--regular" )).ShouldBeTrue();
    }

    // Bob creates 2 independent repositories and publishes them; Tim clones the remotes.
    static async Task<(CKliEnv Bob, CKliEnv Tim, NormalizedPath BobStackPath)> ArrangeBobAndTimAsync( FakeBuildTestEnv testEnv )
    {
        var bobStack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = bobStack.DefaultWorld;
        var bob = world.WorldRoot;
        await world.CreateRepoAsync( "X-One", "v1.0.0" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Two", "v2.0.0" ).ConfigureAwait( false );
        await TouchDevStableAsync( bob.ChangeDirectory( "X-One" ) ).ConfigureAwait( false );
        await TouchDevStableAsync( bob.ChangeDirectory( "X-Two" ) ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish", "--regular" )).ShouldBeTrue();

        var tim = await bobStack.Remotes.CloneAsync( testEnv.Path.AppendPart( "Tim" ),
                                                    allowDuplicateStack: true,
                                                    ( monitor, stackPath, plugins )
                                                        => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                                       .ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "branch", "switch", "stable" )).ShouldBeTrue();
        // The harness commits Tim's <Plugins> configuration in his Stack (re-serialized, its layout differs from
        // Bob's): both Stacks are synchronized so that the World definition is the same on both sides.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "push", "--stack-only" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();
        return (bob, tim, bobStack.StackRoot.AppendPart( StackRepository.PublicStackName ));
    }

    static async Task TouchDevStableAsync( CKliEnv context )
    {
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( context.CurrentDirectory, branchName: "dev/stable" );
    }
}
