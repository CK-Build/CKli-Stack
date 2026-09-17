using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli branch open" and "ckli branch close" both write the BranchModel configuration back: what they write
/// must be what the BranchNamespace constructor reads. A configuration that cannot be parsed is not a bad
/// display, it is a World that no longer opens at all (the BranchModel plugin throws while being instantiated).
/// </summary>
public class BranchOpenCloseTests
{
    /// <summary>
    /// The link type is optional and defaults to CI. BranchLinkType.None is "not specified": back when the
    /// main line was a single string, it had no code string there, so writing it produced "stable  romeo" -
    /// an unparsable configuration that made every subsequent command fail while instantiating the plugin.
    /// </summary>
    [Test]
    public async Task branch_open_without_link_defaults_to_CI_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo" )).ShouldBeTrue();

        BranchModel( stack ).ShouldBe( """
            <BranchModel Root="stable">
              <Prerelease Name="romeo" Link="CI" />
            </BranchModel>
            """ );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "status" )).ShouldBeTrue( "The configuration round trips: the World still opens." );
    }

    /// <summary>
    /// The other half of the default: opening an already opened branch without specifying the link type keeps
    /// the link type it has - it doesn't reset it to CI.
    /// </summary>
    [Test]
    public async Task branch_open_without_link_keeps_the_link_of_an_opened_branch_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();
        var opened = """
            <BranchModel Root="stable">
              <Prerelease Name="romeo" Link="Full" />
            </BranchModel>
            """;
        BranchModel( stack ).ShouldBe( opened );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, r.Root, "branch", "open", "romeo" )).ShouldBeTrue();
        BranchModel( stack ).ShouldBe( opened );
    }

    /// <summary>
    /// A branch is closed by integrating it in its closest OPEN PARENT branch. Since the branch being closed
    /// exists, looking for the closest existing branch from itself answered itself: the branch was merged into
    /// itself and its deletion then failed with "cannot delete branch 'refs/heads/romeo' as it is the current
    /// HEAD of the repository".
    /// </summary>
    [Test]
    public async Task branch_close_integrates_the_branch_in_its_parent_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "romeo", "--link", "Full" )).ShouldBeTrue();
        // The work done on the opened branch is what the close integrates in "stable".
        TestHelper.TouchAndCommit( r.WorkingFolderPath, branchName: null, fileName: "OnRomeo.txt", fileContent: _ => "Done on romeo." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "close", "romeo" )).ShouldBeTrue();

        BranchModel( stack ).ShouldBe( """<BranchModel Root="stable" />""" );
        using( var e = r.CreateEditor() )
        {
            var branches = e.GitRepository.Repository.Branches;
            branches["romeo"].ShouldBeNull( "The closed branch has been deleted." );
            branches["dev/romeo"].ShouldBeNull( "...and so has its \"dev/\" branch." );
            branches["stable"].ShouldNotBeNull()
                              .Tip.Tree["OnRomeo.txt"].ShouldNotBeNull( "The work has been integrated in 'stable'." );
            e.GitRepository.Repository.Head.FriendlyName.ShouldNotBe( "romeo", "The repository is no more on the closed branch." );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "status" )).ShouldBeTrue( "The World still opens." );
    }

    static string? BranchModel( FakeBuildStack stack )
    {
        var root = XDocument.Load( stack.StackRoot.AppendPart( StackRepository.PublicStackName ).AppendPart( "Test.xml" ) ).Root!;
        return root.Element( "Plugins" )?.Element( "BranchModel" )?.ToString();
    }
}
