using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// A publication is serialized across the developers of a Stack by the World's "publish" distributed lock -
/// the very Git reference that "ckli world lock publish" takes. These tests use the fake build harness and
/// clone its remotes twice: two clones are two holders, since the OwnerId of a lease carries the clone.
/// </summary>
public class PublishLockTests
{
    /// <summary>
    /// Tim reserves the publication with "world lock publish"; Bob's "publish" is refused and told who holds
    /// it, then goes through once Tim has released it.
    /// </summary>
    [Test]
    public async Task publish_is_refused_while_another_developer_holds_the_lock_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        // Bob is the primary clone, directly in the working test folder: its fake feeds land beside it.
        var bobStack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = bobStack.DefaultWorld;
        var bob = world.WorldRoot;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        // A first publication is what puts the repository's content on the remotes for Tim to clone. It also
        // settles the Stack's LockPrefix: taking a lock is what determines it, and this is the first one.
        await TouchDevStableAsync( rCore ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish", "--release" )).ShouldBeTrue();

        // Tim: a second working copy of the same remotes. He is one folder deeper than Bob, hence the extra
        // RemoveLastPart so that both resolve the SAME shared "FakeFeed/" folder.
        var tim = await bobStack.Remotes.CloneAsync( testEnv.Path.AppendPart( "Tim" ),
                                                     allowDuplicateStack: true,
                                                     ( monitor, stackPath, plugins )
                                                        => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                                        .ConfigureAwait( false );

        // Tim reserves the publication before starting to work.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "world", "lock", "publish" )).ShouldBeTrue();

        // Bob has something to publish, but not the lock.
        await TouchDevStableAsync( rCore ).ConfigureAwait( false );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish", "--release" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Unable to publish: 'refs/ckli-locks/" )
                                     && l.Contains( "is held by" )
                                     && l.Contains( "ckli world unlock publish" ) );
        }

        // Tim frees it - and Bob's publication, which was blocked by nothing else, goes through.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "world", "unlock", "publish" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish", "--release" )).ShouldBeTrue();
    }

    /// <summary>
    /// The lock a publication takes is released when it ends: the next one - from anybody - must not have to
    /// wait the lease out. This also covers the "nothing to publish" path, which takes the lock all the same
    /// (whether there is anything to do is decided under it).
    /// </summary>
    [Test]
    public async Task publish_releases_its_lock_when_it_ends_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        await TouchDevStableAsync( rCore ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // Nothing left to publish: the lock is taken, the roadmap says there is nothing to do, and it is
        // released again.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--release" )).ShouldBeTrue();

        // Releasing DELETES the reference (that is what keeps the lease chain short), so a lock that is free
        // has no reference at all: "world unlock" finds nothing to release.
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "world", "unlock", "publish" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "is not locked: nothing to release." );
    }

    /// <summary>
    /// Commits a change on the repository's "dev/stable" branch. A successful non-CI build integrates that
    /// branch and deletes it, so it is (re)created on every call.
    /// </summary>
    static async Task TouchDevStableAsync( FakeBuildRepo repo )
    {
        (await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( repo.WorkingFolderPath, branchName: "dev/stable" );
    }
}
