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
/// The "maintenance rebuild" commands. Uses the fake build harness only
/// (<see cref="CKliBuildPluginTestHelperExtensions.CKliCreateFakeBuildTestEnvAsync"/>).
/// </summary>
public class RebuildTests
{
    /// <summary>
    /// A rebuilt published version is locally complete: it is pushed by the next push, even from another
    /// process, so that the other clones don't have to rebuild it.
    /// <para>
    /// This is the first version of the repository: the version sequence rules (no gap, a base version below)
    /// govern the creation of a version, not the rebuild of an existing one.
    /// </para>
    /// </summary>
    [Test]
    public async Task rebuilt_version_is_pushed_by_the_next_push_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        var rCore = await stack.DefaultWorld.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var deferredFile = rCore.WorkingFolderPath.Combine( ".git/CKLI_DEFERRED_PUSH" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "maintenance", "rebuild", "version", "v1.0.0" )).ShouldBeTrue();
        File.ReadAllLines( deferredFile ).ShouldBe( ["+refs/tags/v1.0.0"] );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "push" )).ShouldBeTrue();
        File.Exists( deferredFile ).ShouldBeFalse();
    }

    /// <summary>
    /// A version after a gap in the history can be rebuilt.
    /// </summary>
    [Test]
    public async Task version_after_a_gap_can_be_rebuilt_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rCore = await CreatePublishedV101Async( stack.DefaultWorld ).ConfigureAwait( false );

        // Without v1.0.0, nothing is below v1.0.1: it would be an invalid new version.
        using( var e = rCore.CreateEditor() )
        {
            e.GitRepository.Repository.Tags.Remove( "v1.0.0" );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "maintenance", "rebuild", "version", "v1.0.1" )).ShouldBeTrue();
    }

    /// <summary>
    /// "rebuild old" walks the versions from the oldest up until one can be rebuilt. A version that cannot be
    /// rebuilt is cancelled by a "+invalid" tag: it is pushed by the next push so that the other clones don't
    /// try to rebuild it.
    /// </summary>
    [Test]
    public async Task rebuild_old_tags_are_pushed_by_the_next_push_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rCore = await CreatePublishedV101Async( stack.DefaultWorld ).ConfigureAwait( false );
        var deferredFile = rCore.WorkingFolderPath.Combine( ".git/CKLI_DEFERRED_PUSH" );

        // The oldest one (the first version) is rebuilt: nothing is invalidated.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "maintenance", "rebuild", "old" )).ShouldBeTrue();
        File.ReadAllLines( deferredFile ).ShouldBe( ["+refs/tags/v1.0.0"] );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "push" )).ShouldBeTrue();
        File.Exists( deferredFile ).ShouldBeFalse();

        // When no version can be rebuilt, they are all invalidated.
        rCore.FailBuild = true;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "maintenance", "rebuild", "old" )).ShouldBeTrue();
        File.ReadAllLines( deferredFile ).Order().ShouldBe( ["+refs/tags/v1.0.0+invalid", "+refs/tags/v1.0.1+invalid"] );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "push" )).ShouldBeTrue();
        File.Exists( deferredFile ).ShouldBeFalse();
    }

    static async Task<FakeBuildRepo> CreatePublishedV101Async( FakeBuildWorld world )
    {
        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        // "git branch dev/stable" fails when it already exists: the error is ignored on purpose.
        await CKliCommands.ExecAsync( TestHelper.Monitor, rCore.Root, "exec", "git", "branch", "dev/stable" );
        TestHelper.TouchAndCommit( rCore.WorkingFolderPath, branchName: "dev/stable" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--regular" )).ShouldBeTrue();
        return rCore;
    }
}
