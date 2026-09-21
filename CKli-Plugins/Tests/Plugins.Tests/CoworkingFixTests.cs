using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Co-working on a fix. There is no CI fix build: a fix is shared by pushing its "fix/" branches with
/// <see cref="CKli.HotZone.Plugin.HotZonePlugin.FixPush"/>, and whoever pulls them joins the workflow
/// with "ckli fix start" and rebuilds the packages into his own "$Local" feed with "ckli fix build".
/// <para>
/// This uses the fake build harness, so it exercises the branch and version mechanics rather than an
/// actual NuGet restore of the "local/" packages.
/// </para>
/// </summary>
public class CoworkingFixTests
{
    [Test]
    public async Task a_fix_is_shared_by_pushing_its_branches_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );

        // Tim is the primary clone, directly in the working test folder: its fake feeds land beside it.
        var timStack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = timStack.DefaultWorld;
        var tim = world.WorldRoot;
        var timDisplay = timStack.Screen;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        await world.CreateRepoAsync( "X-Consumer", "v0.1.0", references: [rCore] ).ConfigureAwait( false );

        // Two publications, each triggered by a "feat:" conventional commit that increments the Minor. The
        // second one pushes v1.1 out of the hot zone while leaving it PUBLISHED, and a fix needs both. Only
        // a publication puts a version tag on the remotes - the initial versions the harness creates are
        // local - so fixing v1.0 instead would give Bob a version to fix that his clone cannot even see.
        await TouchDevStableAsync( rCore, "feat: a first feature.", "Tim-feature-1.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "publish", "--release" )).ShouldBeTrue();
        await TouchDevStableAsync( rCore, "feat: a second feature.", "Tim-feature-2.txt" ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "publish", "--release" )).ShouldBeTrue();

        // Tim starts the fix of the published v1.1: one "fix/vMajor.Minor" branch per impacted repository.
        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "start", "v1.1" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
            Fixing 'v1.1.0' on X-Core:
            1 - X-Core     ⎇ fix/v1.1 → v1.1.1 
            2 - X-Consumer ⎇ fix/v0.2 → v0.2.1 
            ❰✓❱

            """ );

        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "fix/v1.1", fileName: "Tim-fix.txt" );

        // The produced versions are "local/" ones: they exist in Tim's "$Local" feed and nowhere else.
        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "build" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
              X-Core     ⎇ fix/v1.1  → v1.1.1
              X-Consumer ⎇ fix/v0.2  → v0.2.1
            ❰✓❱

            """ );

        // "ckli fix push" is what shares the fix, and nothing else does: a "fix/" branch tracks no remote
        // branch yet, so a plain "ckli push" leaves it behind.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "fix", "push" )).ShouldBeTrue();

        // Bob: a second working copy of the same remotes. He is one folder deeper than Tim, hence the extra
        // RemoveLastPart so that both resolve the SAME shared "FakeFeed/" folder.
        var bob = await timStack.Remotes.CloneAsync( testEnv.Path.AppendPart( "Bob" ),
                                                     allowDuplicateStack: true,
                                                     ( monitor, stackPath, plugins )
                                                        => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) )
                                        .ConfigureAwait( false );
        var bobDisplay = (StringScreen)bob.Screen;
        // The bare repositories the fake harness creates keep "main" as their default HEAD, so a fresh clone
        // lands there. Tim never sees this ("ckli repo create" set his working copy up on "stable").
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "branch", "switch", "stable" )).ShouldBeTrue();

        // Bob joins the fix. "fix start" fetches the "fix/v1.*" branches itself and ADOPTS Tim's rather than
        // restarting it: the version to fix is in the branch's history, so CreateTarget does not ask for
        // --move-branch (whose move would discard Tim's commits). Bob gets the same targets and versions.
        var bobCore = bob.ChangeDirectory( "X-Core" );
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobCore, "fix", "start", "v1.1" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
            Fixing 'v1.1.0' on X-Core:
            1 - X-Core     ⎇ fix/v1.1 → v1.1.1 
            2 - X-Consumer ⎇ fix/v0.2 → v0.2.1 
            ❰✓❱

            """ );

        // Tim's fix commit is there: the branch was adopted, not moved back onto the commit to fix.
        File.Exists( bob.CurrentDirectory.AppendPart( "X-Core" ).AppendPart( "Tim-fix.txt" ) ).ShouldBeTrue();

        // "ckli fix build" restores Bob's local state: the same versions, rebuilt into HIS "$Local" feed.
        // No intermediate CI publication was needed for any of this.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobCore, "fix", "build" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
              X-Core     ⎇ fix/v1.1  → v1.1.1
              X-Consumer ⎇ fix/v0.2  → v0.2.1
            ❰✓❱

            """ );
    }

    // The harness commits on "dev/stable": a successful publication integrates it into "stable" and
    // deletes it, so it may have to be created again.
    static async Task TouchDevStableAsync( FakeBuildRepo repo, string? commitMessage = null, string fileName = "CKliTouchAndCommit.txt" )
    {
        // "git branch dev/stable" fails when it already exists: the error is ignored on purpose.
        await CKliCommands.ExecAsync( TestHelper.Monitor, repo.Root, "exec", "git", "branch", "dev/stable" );
        TestHelper.TouchAndCommit( repo.WorkingFolderPath,
                                   branchName: "dev/stable",
                                   commitMessage: commitMessage,
                                   fileName: fileName );
    }
}
