using CK.Core;
using CK.Packaging.Abstractions;
using CKli;
using CKli.Core;
using CKli.Publish.Plugin;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The Published index is derived from the profile files: when two clones both add a profile, their indexes
/// change the same lines and only a "merge=union" declaration in the Stack's ".gitattributes" lets the Stack
/// pull succeed. The union is not an index, so the Publish plugin rebuilds it from the merged profiles.
/// </summary>
public class PublishedIndexMergeTests
{
    [Test]
    public async Task concurrent_profiles_merge_and_the_index_is_rebuilt_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        var bobStack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = bobStack.DefaultWorld;
        var bob = world.WorldRoot;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        await TouchDevStableAsync( bob.ChangeDirectory( "X-Core" ) ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish", "--regular" )).ShouldBeTrue();

        var bobStackFolder = bobStack.StackRoot.AppendPart( StackRepository.PublicStackName );
        File.ReadAllText( bobStackFolder.AppendPart( ".gitattributes" ) )
            .ShouldContain( PublishPlugin.IndexMergeAttributeLine );
        var bobPublished = new PublishedFolder( bobStackFolder.AppendPart( "Published" ) );
        var first = bobPublished.Profiles.Single();

        var tim = await bobStack.Remotes.CloneAsync( testEnv.Path.AppendPart( "Tim" ),
                                                    allowDuplicateStack: true,
                                                    ( monitor, stackPath, plugins )
                                                        => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                                       .ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "branch", "switch", "stable" )).ShouldBeTrue();

        // Bob saves a profile of his own and commits it in his Stack, without pushing it: its version is the
        // greatest one, at the top of the "(stable)" list.
        var bobVersion = SVersion.Parse( "2099.1.0" );
        bobPublished.Add( new PublishedProfile( first.StackUrl, first.World, bobVersion, first.Repositories ) );
        bobPublished.Save();
        using( var git = new LibGit2Sharp.Repository( bobStackFolder ) )
        {
            LibGit2Sharp.Commands.Stage( git, "*" );
            var sig = new LibGit2Sharp.Signature( "Bob", "bob@test", System.DateTimeOffset.Now );
            git.Commit( "Bob's local profile.", sig, sig );
        }

        // Tim publishes (and pushes the Stack): his version is at the top of the same list.
        await TouchDevStableAsync( tim.ChangeDirectory( "X-Core" ) ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "publish", "--regular" )).ShouldBeTrue();
        var timVersion = new PublishedFolder( tim.CurrentStackPath.AppendPart( "Published" ) )
                            .Profiles.Select( p => p.Version ).Max().ShouldNotBeNull();

        // Bob pulls: both indexes changed the same lines, the union merges them and the index is rebuilt.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();
            logs.ShouldContain( "Rebuilt the 'index.json' of 'Published' after a merge of the Stack." );
        }

        var merged = new PublishedFolder( bobStackFolder.AppendPart( "Published" ) );
        merged.Profiles.Select( p => p.Version ).ShouldBe( [bobVersion, timVersion, first.Version] );
        var index = PublishedIndex.Parse( File.ReadAllBytes( merged.IndexFilePath ) );
        index.Alive[PublishedIndex.StableGroupName].ShouldBe( [bobVersion, timVersion, first.Version] );
        File.ReadAllBytes( merged.IndexFilePath ).ShouldBe( merged.CreateIndexUtf8Bytes() );

        // The rebuilt index is committed (here the union is already in the right order, so the merge commit
        // holds it): the Stack is clean.
        using( var git = new LibGit2Sharp.Repository( bobStackFolder ) )
        {
            git.RetrieveStatus().IsDirty.ShouldBeFalse();
        }
    }

    static async Task TouchDevStableAsync( CKliEnv context )
    {
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( context.CurrentDirectory, branchName: "dev/stable" );
    }
}
