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
/// "ckli branch close" works on the repositories of the current directory. It also closes the upstreams whose
/// versions of the branch are consumed by a closed repository (otherwise the parent would receive a reference
/// to a version that nothing ever heals), and the branch leaves the branch model only when no repository of
/// the World has it anymore.
/// </summary>
public class BranchCloseTests
{
    /// <summary>
    /// X-Core &lt;- X-App and a standalone X-Other, all on "mike". X-App's "mike" consumes a "mike" version of X-Core:
    /// closing from X-App also closes X-Core. X-Other keeps "mike", so the branch model keeps it too.
    /// </summary>
    [Test]
    public async Task closing_a_downstream_closes_the_upstreams_whose_versions_it_consumes_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var core = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var app = await world.CreateRepoAsync( "X-App", "v1.0.0", default, core ).ConfigureAwait( false );
        var other = await world.CreateRepoAsync( "X-Other", "v1.0.0" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "mike", "--link", "Full" )).ShouldBeTrue();
        // On "dev/mike" (checked out by the open).
        app.AddOrUpdateReference( core, "1.0.1-mike" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, app.Root, "branch", "close", "mike" )).ShouldBeTrue();
            logs.ShouldContain( "Also closing 'mike' in 'X-Core': its 'mike' versions are consumed by 'X-App'." );
            logs.ShouldContain( "Branch 'mike' closed in 2 repositories, still opened in 1 repository." );
        }
        HasBranch( core, "mike" ).ShouldBeFalse();
        HasBranch( app, "mike" ).ShouldBeFalse();
        HasBranch( other, "mike" ).ShouldBeTrue();
        BranchModel( stack )!.ShouldContain( "mike" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, other.Root, "branch", "close", "mike" )).ShouldBeTrue();
        HasBranch( other, "mike" ).ShouldBeFalse();
        BranchModel( stack ).ShouldBe( """<BranchModel Root="stable" />""", "No repository has 'mike' anymore." );
    }

    /// <summary>
    /// Closing an upstream leaves its downstreams on the branch: they heal on their next build, that reads the
    /// upstream from its parent branch.
    /// </summary>
    [Test]
    public async Task closing_an_upstream_leaves_its_downstreams_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var core = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var app = await world.CreateRepoAsync( "X-App", "v1.0.0", default, core ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "mike", "--link", "Full" )).ShouldBeTrue();
        app.AddOrUpdateReference( core, "1.0.1-mike" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, core.Root, "branch", "close", "mike" )).ShouldBeTrue();
            logs.ShouldNotContain( t => t.StartsWith( "Also closing" ) );
            logs.ShouldContain( "Branch 'mike' closed in 1 repository, still opened in 1 repository." );
        }
        HasBranch( core, "mike" ).ShouldBeFalse();
        HasBranch( app, "mike" ).ShouldBeTrue();
        BranchModel( stack )!.ShouldContain( "mike" );
    }

    /// <summary>
    /// Only the closed branch and the branch that receives it matter, and only in the repositories that are closed:
    /// "juliet" (a child of "mike") is opened everywhere and has an issue in a repository that is not concerned.
    /// </summary>
    [Test]
    public async Task other_branches_and_other_repositories_are_irrelevant_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var core = await world.CreateRepoAsync( "X-Core", "v1.0.0" ).ConfigureAwait( false );
        var other = await world.CreateRepoAsync( "X-Other", "v1.0.0" ).ConfigureAwait( false );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "mike", "--link", "Full" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "open", "juliet", "--link", "Full" )).ShouldBeTrue();
        // A commit on the base "juliet" branch: its "dev/juliet" is Desynchronized in X-Other.
        TestHelper.TouchAndCommit( other.WorkingFolderPath, branchName: "juliet" );
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "issue" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( "dev/juliet", customMessage: "The arranged issue exists." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, core.Root, "branch", "close", "mike" )).ShouldBeTrue();
        HasBranch( core, "mike" ).ShouldBeFalse();
        HasBranch( core, "juliet" ).ShouldBeTrue();
        HasBranch( other, "mike" ).ShouldBeTrue();
    }

    static bool HasBranch( FakeBuildRepo repo, string name )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Branches[name] != null;
    }

    static string? BranchModel( FakeBuildStack stack )
    {
        var root = XDocument.Load( stack.StackRoot.AppendPart( StackRepository.PublicStackName ).AppendPart( "Test.xml" ) ).Root!;
        return root.Element( "Plugins" )?.Element( "BranchModel" )?.ToString();
    }
}
