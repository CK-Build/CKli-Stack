using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Two developers working on the same stack. This uses the fake build harness
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>) and clones its
/// remotes twice: <see cref="FakeBuildStack.Remotes"/> is what makes a second working copy possible.
/// </summary>
public class CoworkingTests
{
    /// <summary>
    /// Bob and Tim share the remotes. Tim publishes first, so Bob must incorporate Tim's work before he can
    /// publish his own: until he does, his repository is reported as having desynchronized branches.
    /// </summary>
    [TestCase( true )]
    [TestCase( false )]
    public async Task coworking_Async( bool useCheckout )
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        // Bob is the primary clone, directly in the working test folder: its fake feeds land beside it.
        var bobStack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = bobStack.DefaultWorld;
        var bob = world.WorldRoot;
        var bobDisplay = bobStack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-PerfectEvent", "v0.3.3", references: [rCore] ).ConfigureAwait( false );

        // Nothing the harness did is on the remotes yet, and Tim needs 2 different things from them:
        //  - the stack definition, which is what lists the repositories: "ckli push --stack-only" sends it
        //    without any publication (a plain "ckli push" adds the Repos, but only their branches that
        //    already track a remote one - here "main" alone);
        //  - the repositories' content, which the harness leaves on "dev/stable": their "stable" holds
        //    nothing but the "Initializing 'stable'." commit, and pushing it as is would give Tim 2 empty
        //    repositories.
        // Only a publication produces the second one, so that is what Bob does here. It also pushes the
        // stack definition, which is why no explicit "push --stack-only" is needed.
        await TouchDevStableAsync( bob.ChangeDirectory( "X-Core" ), useCheckout, fileName: "Bob-init.txt" ).ConfigureAwait( false );

        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "publish" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
            1 -  X-Core         v1.0.1 → ⏚/v1.0.2 (CodeChange)   
            2 -  X-PerfectEvent v0.3.3 → ⏚/v0.3.4 (UpstreamBuild)
            Required build for 2 repositories across the 2 repositories and 2 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Tim: a second working copy of the same remotes. He is one folder deeper than Bob, hence the extra
        // RemoveLastPart so that both resolve the SAME shared "FakeFeed/" folder.
        var tim = await bobStack.Remotes.CloneAsync( testEnv.Path.AppendPart( "Tim" ),
                                                    allowDuplicateStack: true,
                                                    ( monitor, stackPath, plugins )
                                                        => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                                       .ConfigureAwait( false );
        var timDisplay = (StringScreen)tim.Screen;

        // The bare repositories the fake harness creates keep "main" as their default HEAD, so a fresh clone
        // lands there. Bob never sees this ("ckli repo create" set his working copy up on "stable").
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "branch", "switch", "stable" )).ShouldBeTrue();

        // Tim clones a stack without any issue.
        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "issue" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // Bob starts to work on X-PerfectEvent with a BREAKING change (the "!" in "fix!:").
        var bobPivot = bob.ChangeDirectory( "X-PerfectEvent" );
        await TouchDevStableAsync( bobPivot,
                                   useCheckout,
                                   "fix!: This is a breaking change because of the exclamation mark.",
                                   "Bob-work.txt" ).ConfigureAwait( false );

        // But Tim publishes a plain fix of the same repository first.
        var timPivot = tim.ChangeDirectory( "X-PerfectEvent" );
        await TouchDevStableAsync( timPivot, useCheckout, fileName: "Tim-work.txt" ).ConfigureAwait( false );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPivot, "publish" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
              - →·   X-Core         v1.0.2
            1 -  ⊙   X-PerfectEvent v0.3.4 → ⏚/v0.3.5 (CodeChange)
            Required build for 1 from the single pivot out of 2 repositories and 1 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Bob pulls. His "dev/stable" is no longer a tracking branch: the remote one has been pruned.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();

        // Bob cannot publish his contribution, in CI or not: he must incorporate Tim's work first.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPivot, "publish", "--ci", "-d" )).ShouldBeFalse();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPivot, "publish", "-d" )).ShouldBeFalse();

        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
            > X-PerfectEvent (1)
            │ > Desynchronized branches.
            │ │ - Branch 'stable' has 1 commits that must be in 'dev/stable'.
            │ │ Base branches can be merged without conflict into the desynchronized branches.
            ❰✓❱

            """ );

        // Tim publishes a fix of the version he just published, this time in CI.
        await TouchDevStableAsync( timPivot, useCheckout, fileName: "Tim-work.txt" ).ConfigureAwait( false );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPivot, "publish", "--ci" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
              - →·   X-Core         v1.0.2
            1 -  ⊙   X-PerfectEvent v0.3.5 → ⏚/v0.3.6--ci.1 (CodeChange)
            Required build for 1 from the single pivot out of 2 repositories and 1 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Bob pulls again. The remote "dev/stable" now exists (Tim pushed in CI), so the pull synchronizes
        // Bob's "stable" into his "dev/stable" and the issue is gone.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();

        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // Bob publishes his breaking change. In a 0.X.Y version only the Minor is incremented.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPivot, "publish" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
              - →·   X-Core         v1.0.2
            1 -  ⊙   X-PerfectEvent v0.3.5 → ⏚/v0.4.0 (CodeChange)
            Required build for 1 from the single pivot out of 2 repositories and 1 can be published.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

        // Tim creates a "dev/stable" before pulling. This is useless and the issue reflects it.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "branch", "switch", "dev/stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPivot, "pull" )).ShouldBeTrue();

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPivot, "issue" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
            > X-PerfectEvent (1)
            │ > Removable branches.
            │ │ - 'dev/stable' is merged into 'stable'.
            │ │ It can be deleted.
            ❰✓❱

            """ );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "issue", "--fix" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
            ❰✓❱

            """ );
    }

    /// <summary>
    /// Commits a change on the repository's "dev/stable" branch, creating that branch either through ckli
    /// (<paramref name="useCheckout"/>) or directly through git.
    /// </summary>
    static async Task TouchDevStableAsync( CKliEnv context,
                                           bool useCheckout,
                                           string? commitMessage = null,
                                           string fileName = "CKliTouchAndCommit.txt" )
    {
        if( useCheckout )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "switch", "dev/stable", "-c" )).ShouldBeTrue();
        }
        else
        {
            // "git branch dev/stable" fails when it already exists: the error is ignored on purpose.
            await CKliCommands.ExecAsync( TestHelper.Monitor, context, "exec", "git", "branch", "dev/stable" );
        }
        TestHelper.TouchAndCommit( context.CurrentDirectory,
                                   branchName: "dev/stable",
                                   commitMessage: commitMessage,
                                   fileName: fileName );
    }
}
