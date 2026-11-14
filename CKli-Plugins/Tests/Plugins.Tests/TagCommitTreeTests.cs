using CK.Core;
using CKli;
using CKli.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The walk of a <see cref="CKli.VersionTag.Plugin.TagCommitTree"/>. These tests use the fake build harness
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>) and shape the history directly.
/// </summary>
public class TagCommitTreeTests
{
    /// <summary>
    /// A merge whose parents are the LastStable and one of its children that bears a hot version (a branch that merged
    /// "stable" and "dev/stable" separately, CK-AppIdentity-Transport, 2026-11-14): both are met at the same level of
    /// the walk, the LastStable first. It is not the last entry of the tree's content, and doesn't have to be.
    /// </summary>
    [Test]
    public async Task the_LastStable_and_a_hot_version_at_the_same_level_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        var repo = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        using( var e = repo.CreateEditor() )
        {
            var git = e.GitRepository.Repository;
            var signature = new Signature( "Test", "test@test.org", DateTimeOffset.Now );
            var initialTag = git.Tags["v1.0.0"].ShouldNotBeNull();
            var lastStable = (Commit)initialTag.PeeledTarget;
            git.Branches["dev/stable"].Tip.Sha.ShouldBe( lastStable.Sha );
            // A child of the LastStable that bears a CI version.
            var child = git.ObjectDatabase.CreateCommit( signature, signature, "A CI build.", lastStable.Tree, [lastStable], prettifyMessage: true );
            git.Tags.Add( "v1.0.1--ci.1", child, signature, initialTag.Annotation.Message );
            // The tip merges the LastStable (first parent) and its child.
            var merge = git.ObjectDatabase.CreateCommit( signature, signature, "Merged.", lastStable.Tree, [lastStable, child], prettifyMessage: true );
            git.Refs.UpdateTarget( git.Branches["dev/stable"].Reference, merge.Id, null );
        }

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  X-Core v1.0.0
            There is nothing to build across the 1 repositories and nothing to publish.
            (Use '--ci.0' to build a CI version from the repository that already carries a released version.)
            ❰✓❱

            """ );
    }
}
