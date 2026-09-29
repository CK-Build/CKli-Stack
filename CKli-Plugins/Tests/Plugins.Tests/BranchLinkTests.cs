using CK.Core;
using CKli;
using CKli.BranchModel.Plugin;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class BranchLinkTests
{
    static void TouchFile( GitRepository repo, string fileNameSuffix = "", string? content = null )
    {
        var p = repo.WorkingFolder.AppendPart( $"_touch_file_{fileNameSuffix}.txt" );
        if( content != null )
        {
            File.WriteAllText( p, content );
        }
        else
        {
            content = Util.GetRandomBase64UrlString( 10 ) + Environment.NewLine;
            if( !File.Exists( p ) )
            {
                File.WriteAllText( p, content );
            }
            else
            {
                File.WriteAllText( p, File.ReadAllText( p ) + content );
            }
        }
    }

    [Test]
    public void playing_with_ahead()
    {
        var folder = TestHelper.InitializeClonedFolder( clearStackRegistryFile: false );
        using var repo = folder.CreateOrphanGitRepository( "SomeRepo" );
        var monitor = TestHelper.Monitor;

        // An empty commit is a commit: the ahead branch that holds only the empty initialization commit is not
        // useless, and integrating it fast-forwards the base branch. This is about commits, not content: an empty
        // "Producing 'vX' from unchanged head." commit carries a version that must not be lost.
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.EnsureAhead( repo, withEmptyInitializationCommit: true ).ShouldNotBeNull();
            mainLink.Ahead.ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );
            var emptyCommit = mainLink.Ahead.Tip;

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldBe( emptyCommit.Sha, "Fast-forwarded to the empty commit." );
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );
        }

        // Integrating ahead (ahead is checked out).
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();

            mainLink = mainLink.EnsureAhead( repo );
            mainLink.Ahead.ShouldNotBeNull();

            repo.Checkout( monitor, mainLink.Ahead ).ShouldBeTrue();
            repo.CurrentBranchName.ShouldBe( "dev/main" );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            TouchFile( repo, "1" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (1)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldNotBe( mainBranch.Tip.Sha );
            // When removed, the fact that is was the checked out branch is handled: the base branch
            // is checked out.
            repo.CurrentBranchName.ShouldBe( "main" );
            mainLink.Ahead.ShouldBeNull();

            // main -> + "Some message. (1)"
            //         |
            //         + "Initializing 'dev/main'."
            //         |
            //         + "Initial commit automatically created."
            mainLink.Branch.Commits.Count().ShouldBe( 3 );
        }

        // Integrating ahead (while base branch is checked out).
        // => Same as previous.
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();

            mainLink = mainLink.EnsureAhead( repo );
            mainLink.Ahead.ShouldNotBeNull();

            repo.Checkout( monitor, mainLink.Ahead ).ShouldBeTrue();
            repo.CurrentBranchName.ShouldBe( "dev/main" );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            TouchFile( repo, "2" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (2)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldNotBe( mainBranch.Tip.Sha );
            // When removed, the fact that is was the checked out branch is handled: the base branch
            // is checked out.
            repo.CurrentBranchName.ShouldBe( "main" );
            mainLink.Ahead.ShouldBeNull();

            // main -> + "Some message. (2)"
            //         |
            //         + "Some message. (1)"
            //         |
            //         + "Initializing 'dev/main'."
            //         |
            //         + "Initial commit automatically created."
            mainLink.Branch.Commits.Count().ShouldBe( 4 );
        }

        // Integrating ahead (amending the "empty ahead commit").
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();

            mainLink = mainLink.EnsureAhead( repo, withEmptyInitializationCommit: true );
            mainLink.Ahead.ShouldNotBeNull();
            repo.Checkout( monitor, mainLink.Ahead );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );
            TouchFile( repo, "3" );
            repo.Commit( monitor, "Some message. (3)", CommitBehavior.AmendIfPossibleAndOverwritePreviousMessage ).ShouldBe( CommitResult.Amended );
            mainLink = mainLink.CommitAhead( monitor, repo, "Unused message because there's nothing to commit." ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldNotBe( mainBranch.Tip.Sha );
            mainLink.Ahead.ShouldBeNull();

            // main -> + "Some message. (3)"
            //         |
            //         + "Some message. (2)"
            //         |
            //         + "Some message. (1)"
            //         |
            //         + "Initializing 'dev/main'."
            //         |
            //         + "Initial commit automatically created."
            mainLink.Branch.Commits.Count().ShouldBe( 5 );
        }
    }

    [Test]
    public void link_desynchronization()
    {
        var folder = TestHelper.InitializeClonedFolder( clearStackRegistryFile: false );
        using var repo = folder.CreateOrphanGitRepository( "SomeRepo" );
        var monitor = TestHelper.Monitor;

        // Required to restore a neutral checked out branch (to be able to destroy "main" at the end of each block of tests).
        var root = repo.EnsureBranch( monitor, "root" ).ShouldNotBeNull();
        
        // Regular desynchronization.
        {
            // Ensure ahead (creates the "empty ahead commit").
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo, withEmptyInitializationCommit: true )
                                     .ShouldNotBeNull();
            // The "empty ahead commit" is a commit: the ahead branch is not useless.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo );
            repo.Commit( monitor, "On Base! (1)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Committed );

            // Refresh.
            mainLink = mainLink.Refresh( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            // The ahead branch now has the same content as its base, but it has its own commits.
            mainLink = mainLink.SynchronizeAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( monitor, repo ).ShouldBeNull();
        }

        // Desynchronized and commits in ahead without conflicts: SynchronizeAhead then IntegrateAhead.
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo, "One" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (2)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo, "Two" );
            repo.Commit( monitor, "On Base! (2)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Committed );

            // Refresh: Desynchronized!
            mainLink = mainLink.Refresh( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            mainLink = mainLink.SynchronizeAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( monitor, repo ).ShouldBeNull();
        }

        // Desynchronized and commits in ahead without conflicts: IntegrateAhead then SynchronizeAhead.
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo, "One" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (3)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo, "Two" );
            repo.Commit( monitor, "On Base! (3)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Committed );

            // Refresh: Desynchronized!
            mainLink = mainLink.Refresh( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // The head integration solved everything, Synchronize is a no-op.
            var newMainLink = mainLink.SynchronizeAhead( monitor, repo );
            newMainLink.ShouldBeSameAs( mainLink );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( monitor, repo ).ShouldBeNull();
        }

        // When same content is eventually in base and ahead, through different commits: the link is Desynchronized
        // (this is about commits, not content) and synchronizing it creates an empty merge commit.
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo, content: "Kif Kif" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (4)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo, content: "Kif Kif" );
            repo.Commit( monitor, "On Base! (4)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Committed );

            // Refresh: the contents are the same but the commits differ.
            mainLink = mainLink.Refresh( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            // The (empty) merge commit makes the base branch reachable from the ahead one: skipping it
            // would leave the link Desynchronized forever.
            var baseTip = mainLink.Branch.Tip;
            mainLink = mainLink.SynchronizeAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldNotBeNull().Tip.Parents.Select( p => p.Sha ).ShouldContain( baseTip.Sha );
            mainLink.Ahead.Tip.Tree.Sha.ShouldBe( baseTip.Tree.Sha );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // The integration fast-forwards the base branch.
            var aheadTip = mainLink.Ahead.Tip;
            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldBe( aheadTip.Sha );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( monitor, repo ).ShouldBeNull();
        }

        // Synchronization conflict (manual resolution required).
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (5)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo );
            repo.Commit( monitor, "On Base! (5)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Committed );

            // Refresh: Desynchronized!
            mainLink = mainLink.Refresh( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            // But there's a merge conflict that must be manually resolved.
            mainLink.SynchronizeAhead( monitor, repo ).ShouldBeNull();

            // Cleanup "dev/main" and "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Ahead );
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( monitor, repo ).ShouldBeNull();
        }

    }


    [Test]
    public void when_ahead_is_on_Remote()
    {
        var folder = TestHelper.InitializeClonedFolder( clearStackRegistryFile: false );
        var monitor = TestHelper.Monitor;

        // Ensures that we have a Remote with a "main" and "dev/main" branches.
        Uri remoteUrl;
        {
            using var remote = folder.CreateBareRepository( "Remote", out remoteUrl );
            var mainBranch = remote.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.EnsureAhead( remote, withEmptyInitializationCommit: false );
        }

        using var repo = folder.CloneGitRepository( "SomeRepo", remoteUrl );

        {
            var main = repo.Repository.Branches["main"].ShouldNotBeNull( "Because it is the default branch." );
            main.IsTracking.ShouldBeTrue();
            main.IsCurrentRepositoryHead.ShouldBeTrue();

            // Creating a link has no impact on the repository ("dev/main" is not created).
            var mainLink = BranchLink.Create( main, "dev/main" );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            var remoteDevMain = repo.Repository.Branches["refs/remotes/origin/dev/main"];
            remoteDevMain.ShouldNotBeNull();
            var devMain = repo.Repository.Branches["dev/main"];
            devMain.ShouldBeNull( "The remote branch has been cloned (but is not tracked)." );

            TestHelper.TouchAndCommit( repo.WorkingFolder, null );
        }
    }
}
