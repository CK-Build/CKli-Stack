using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// Publication of a branch that requires "local/" releases of its parent branches. These tests use the fake build
/// harness only (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class IndirectPublicationTests
{
    /// <summary>
    /// The releases of the parent branches are published first, and the releases of the published branch are not
    /// among them even though they consume them: the roadmap publishes them. Including them published them twice,
    /// the second time failing with "Missing N local packages" since the first one removed their local artifacts.
    /// </summary>
    [Test]
    public async Task branch_releases_are_not_published_as_parent_releases_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var context = world.WorldRoot;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.0", references: [rCore] ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--regular" )).ShouldBeTrue();

        // Only X-Consumer has a "juliet" branch: X-Core is seen from "juliet" on "stable".
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConsumer.Root, "branch", "open", "juliet", "--link", "CI" )).ShouldBeTrue();

        // A pending "local/" release of X-Core on "stable" (and of its X-Consumer consumer on "stable")...
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--regular", "--branch", "stable" )).ShouldBeTrue();

        // ...consumed by a pending "local/" release of X-Consumer on "juliet": it is a consumer of the X-Core
        // release, but it belongs to the published branch.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--regular", "--branch", "juliet" )).ShouldBeTrue();

        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--branch", "juliet", "--regular", "--dry-run" )).ShouldBeTrue();
        var gate = stack.Screen.ToString();
        gate.ShouldContain( "Also publishing 2 releases from parent branches:" );
        // The parent releases have been built with their tests: the publication doesn't run them again.
        gate.ShouldContain( "  stable  ✓ X-Core/local/v1.0.1, ✓ X-Consumer/local/v0.1.1" );
        gate.ShouldNotContain( "juliet  X-Consumer" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--branch", "juliet", "--regular" )).ShouldBeTrue();
        using( var e = rCore.CreateEditor() )
        {
            e.GitRepository.Repository.Tags["v1.0.1"].ShouldNotBeNull();
        }
        using( var e = rConsumer.CreateEditor() )
        {
            e.GitRepository.Repository.Tags["v0.1.1"].ShouldNotBeNull();
            e.GitRepository.Repository.Tags["v0.1.1-juliet"].ShouldNotBeNull();
        }
    }

    /// <summary>
    /// A closed publication gate is known from the roadmap alone: the publication is refused before any build. A
    /// regular build integrates the "dev/" branches into their base branch, so a refusal after the builds left the
    /// base branches moved and new version tags (nothing pushed, but not what the developer left).
    /// Here the gate is closed by a required release of the parent branch whose local packages are gone.
    /// </summary>
    [Test]
    public async Task a_closed_gate_refuses_before_any_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var context = world.WorldRoot;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var rConsumer = await world.CreateRepoAsync( "X-Consumer", "v0.1.0", references: [rCore] ).ConfigureAwait( false );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--regular" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rConsumer.Root, "branch", "open", "juliet", "--link", "CI" )).ShouldBeTrue();

        // A pending "local/" release of X-Core on "stable": publishing "juliet" requires it (and X-Consumer on
        // "juliet" must be built: it consumes the X-Core release).
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--regular", "--branch", "stable" )).ShouldBeTrue();

        // Its packages are gone from "$Local": only a rebuild can fix it, the gate is closed.
        var lost = Directory.EnumerateFiles( stack.StackRoot, "*.nupkg", SearchOption.AllDirectories )
                            .Where( f => Path.GetFileName( f ).Equals( "X.Core.1.0.1.nupkg", StringComparison.OrdinalIgnoreCase ) )
                            .ToList();
        lost.ShouldNotBeEmpty();
        foreach( var f in lost ) File.Delete( f );

        var coreRefs = GetRefs( rCore );
        var consumerRefs = GetRefs( rConsumer );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--branch", "juliet", "--regular" )).ShouldBeFalse();
            logs.ShouldContain( "Unable to publish: the profile of branch 'juliet' would be incoherent. Nothing has been built." );
        }
        // Nothing has been built: no integration, no commit, no version tag.
        GetRefs( rCore ).ShouldBe( coreRefs );
        GetRefs( rConsumer ).ShouldBe( consumerRefs );

        static Dictionary<string, string> GetRefs( FakeBuildRepo r )
        {
            using var e = r.CreateEditor();
            return e.GitRepository.Repository.Refs.ToDictionary( x => x.CanonicalName, x => x.TargetIdentifier );
        }
    }
}
