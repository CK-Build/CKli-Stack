using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli build" skips the "Deployment/GenerateAssets.cs" scripts unless "--with-assets" is specified: without values,
/// every asset is generated, otherwise the values are given to the scripts. The version tag records the request
/// (<c>BuildContentInfo.AssetsNotGenerated</c> and <c>AssetsRequest</c>) and a version that doesn't satisfy a request is
/// built again (<c>MustBuildReason.MissingAssets</c>). A publication always requires every asset.
/// </summary>
public class WithAssetsTests
{
    [Test]
    public async Task a_version_built_without_its_assets_is_built_again_by_with_assets_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rApp = await world.CreateRepoAsync( "X-App", "v1.0.0" ).ConfigureAwait( false );
        WriteScript( rApp );

        // A plain build doesn't run the script.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        rApp.LastWithAssets.ShouldBeNull();

        // "--with-assets": the version has no assets, it is built again and every asset is requested.
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "MissingAssets" );
        rApp.LastWithAssets.ShouldNotBeNull().ShouldBeEmpty();

        // Everything is there: nothing to build, for any request.
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets", "Normal", "--dry-run" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "There is nothing to build" );
    }

    [Test]
    public async Task a_partial_request_is_recorded_and_a_publication_requires_every_asset_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rApp = await world.CreateRepoAsync( "X-App", "v1.0.0" ).ConfigureAwait( false );
        WriteScript( rApp );

        // The values are sorted and deduplicated.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets", "Normal", "Advanced", "Normal" )).ShouldBeTrue();
        rApp.LastWithAssets.ShouldNotBeNull().ShouldBe( ["Advanced", "Normal"] );

        // A subset of the recorded request is satisfied.
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets", "Normal", "--dry-run" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "There is nothing to build" );

        // Another value is not.
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets", "Other", "--dry-run" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "MissingAssets" );

        // "Everything" is not satisfied by a partial request: CKli doesn't know what "everything" is.
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets", "--dry-run" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "MissingAssets" );

        // A publication requires every asset: the version is built again before being published.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
        rApp.LastWithAssets.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Test]
    public async Task without_script_there_is_nothing_to_generate_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rLib = await world.CreateRepoAsync( "X-Lib", "v1.0.0" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rLib.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();

        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets", "--dry-run" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "There is nothing to build" );
    }

    [Test]
    public async Task a_value_with_a_white_space_or_a_comma_is_an_error_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        await world.CreateRepoAsync( "X-App", "v1.0.0" ).ConfigureAwait( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--with-assets", "A,B" )).ShouldBeFalse();
            logs.ShouldContain( t => t.StartsWith( "Invalid '--with-assets' values: 'A,B'." ) );
        }
    }

    static void WriteScript( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        TestHelper.TouchAndCommit( e.GitRepository.Repository,
                                   "Deployment/GenerateAssets.cs",
                                   fileContent: _ => "return 0;\r\n" );
    }
}
