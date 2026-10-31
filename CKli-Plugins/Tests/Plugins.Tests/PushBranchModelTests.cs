using CK.Monitoring;
using CKli;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli push" pushes the local branches that track a remote branch. A branch of the branch model (or its "dev/" branch)
/// that tracks nothing because it has been created by a raw "git checkout -b" is pushed anyway: the BranchModel
/// plugin requires it through the Pushing event. Other untracked branches stay as-is.
/// </summary>
public class PushBranchModelTests
{
    [Test]
    public async Task an_untracked_branch_model_branch_is_pushed_and_tracks_its_origin_branch_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var core = world.WorldRoot.CurrentDirectory.AppendPart( "X-Core" );

        // Raw git: neither "dev/stable" nor "not-in-model" track a remote branch.
        using( var git = new LibGit2Sharp.Repository( core ) )
        {
            var tip = git.Branches["stable"].Tip;
            var dev = git.Branches["dev/stable"];
            if( dev != null )
            {
                if( dev.IsTracking ) git.Branches.Update( dev, u => u.TrackedBranch = null );
            }
            else
            {
                git.Branches.Add( "dev/stable", tip );
            }
            git.Branches.Add( "not-in-model", tip );
            git.Branches["dev/stable"].IsTracking.ShouldBeFalse();
        }

        // A GrandOutput memory collector, not TestHelper.Monitor.CollectTexts: the repositories are pushed
        // on the ActivityMonitorAsyncPool's own monitors, which a monitor scoped client never sees.
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "push", "--all" )).ShouldBeTrue();
            logs.ExtractCurrentTexts()
                .ShouldContain( "Branch 'dev/stable' of 'X-Core' has no tracked branch: it now tracks 'origin/dev/stable'." );
        }

        using( var git = new LibGit2Sharp.Repository( core ) )
        {
            var dev = git.Branches["dev/stable"];
            dev.TrackedBranch.ShouldNotBeNull().CanonicalName.ShouldBe( "refs/remotes/origin/dev/stable" );
            git.Branches["not-in-model"].IsTracking.ShouldBeFalse( "Branches outside the branch model stay as-is." );
            using var remote = new LibGit2Sharp.Repository( new Uri( git.Network.Remotes["origin"].Url ).LocalPath );
            remote.Branches["dev/stable"].ShouldNotBeNull().Tip.Sha.ShouldBe( dev.Tip.Sha );
            remote.Branches["not-in-model"].ShouldBeNull();
        }
    }
}
