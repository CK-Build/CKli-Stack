using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The "#:package Name@Version" directives of "Deployment/GenerateAssets.cs" are "second class" package references
/// (<c>GitSolutionContent.ScriptConsumed</c>): they order the builds and are upgraded when their solution is built,
/// but they never trigger a build on their own.
/// The "//ck-package Name@Version" directives of the same script are first class references (<c>GitSolutionContent.Consumed</c>):
/// they also trigger a build.
/// </summary>
public class GenerateAssetsReferenceTests
{
    /// <summary>
    /// X-Down is created before X-Up (a lower index) and its script references X-Up's package.
    /// <list type="bullet">
    ///   <item>A build of X-Up doesn't build X-Down: the script keeps its version.</item>
    ///   <item>A build of X-Down (for its own reason) upgrades the script to X-Up's last version.</item>
    ///   <item>When both are built, X-Up is built first and X-Down's script gets the version just built.</item>
    /// </list>
    /// </summary>
    [Test]
    public async Task a_script_reference_orders_the_builds_and_is_upgraded_but_never_triggers_a_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rDown = await world.CreateRepoAsync( "X-Down", "v1.0.0" ).ConfigureAwait( false );
        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var upPackage = rUp.DefaultProjectName;
        WriteScript( rDown, upPackage, "1.0.0" );

        // X-Down has a code change (its script): it is built, X-Up is not.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();

        // X-Up is modified: X-Down is not built because of its script.
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        var screen = stack.Screen.ToString();
        screen.ShouldContain( "Required build for 1 " );
        screen.ShouldContain( " i  X-Up ⏚/v" );
        screen.ShouldNotContain( " i  X-Down ⏚/v", customMessage: "A script reference never triggers a build." );
        ReadScript( rDown ).ShouldContain( $"#:package {upPackage}@1.0.0" );

        // X-Down is modified: its build upgrades the script to the last version of X-Up.
        TestHelper.TouchAndCommit( rDown.WorkingFolderPath, branchName: null );
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        stack.Screen.ToString().ShouldContain( " i  X-Down ⏚/v" );
        ReadScript( rDown ).ShouldContain( $"#:package {upPackage}@{LastVersion( rUp )}" );

        // Both are modified: X-Up is built first (X-Down has the lower index) and X-Down's script gets its new version.
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );
        TestHelper.TouchAndCommit( rDown.WorkingFolderPath, branchName: null );
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        var plan = stack.Screen.ToString();
        plan.IndexOf( "X-Up" ).ShouldBeLessThan( plan.IndexOf( "X-Down" ), "X-Up is ranked before X-Down." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        ReadScript( rDown ).ShouldContain( $"#:package {upPackage}@{LastVersion( rUp )}" );
    }

    /// <summary>
    /// The one version per package identifier rule covers the script: a "#:package" that disagrees with a project
    /// of the repository is an error, as two projects that disagree are.
    /// </summary>
    [Test]
    public async Task a_script_version_that_differs_from_the_projects_is_an_error_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var rDown = await world.CreateRepoAsync( "X-Down", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        WriteScript( rDown, rUp.DefaultProjectName, "0.9.0" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeFalse();
            logs.ShouldContain( t => t.Contains( $"Package '{rUp.DefaultProjectName}' is referenced in two versions by this repository:" )
                                     && t.Contains( "Deployment/GenerateAssets.cs" ) );
        }
    }

    /// <summary>
    /// X-Down is created before X-Up (a lower index) and its script has a "//ck-package" of X-Up's package.
    /// Unlike a "#:package", a build of X-Up builds X-Down and upgrades its script.
    /// </summary>
    [Test]
    public async Task a_ck_package_reference_orders_the_builds_is_upgraded_and_triggers_a_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rDown = await world.CreateRepoAsync( "X-Down", "v1.0.0" ).ConfigureAwait( false );
        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var upPackage = rUp.DefaultProjectName;
        WriteScriptLines( rDown, "#:package Some.Tool@1.0.0", $"//ck-package {upPackage}@1.0.0" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();

        // X-Up is modified: X-Up is built first, then X-Down because of its "//ck-package" that is upgraded.
        TestHelper.TouchAndCommit( rUp.WorkingFolderPath, branchName: null );
        stack.Screen.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build", "--dry-run" )).ShouldBeTrue();
        var plan = stack.Screen.ToString();
        plan.ShouldContain( "Required build for 2 " );
        plan.IndexOf( "X-Up" ).ShouldBeLessThan( plan.IndexOf( "X-Down" ), "X-Up is ranked before X-Down." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
        var upVersion = LastVersion( rUp );
        var script = ReadScript( rDown );
        script.ShouldContain( $"//ck-package {upPackage}@{upVersion}\r\n" );
        // The build adds it to the consumed packages of its version tag ("dotnet package list" doesn't see it).
        LastTagAnnotation( rDown ).ShouldContain( $"{upPackage}@{upVersion}" );
        script.ShouldContain( "#:package Some.Tool@1.0.0\r\n", customMessage: "An external package without update is left as it is." );
    }

    /// <summary>
    /// The one version per package identifier rule covers the "//ck-package" directives.
    /// </summary>
    [Test]
    public async Task a_ck_package_version_that_differs_from_the_projects_is_an_error_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rUp = await world.CreateRepoAsync( "X-Up", "v1.0.0" ).ConfigureAwait( false );
        var rDown = await world.CreateRepoAsync( "X-Down", "v1.0.0", references: [rUp] ).ConfigureAwait( false );
        WriteScriptLines( rDown, $"//ck-package {rUp.DefaultProjectName}@0.9.0" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeFalse();
            logs.ShouldContain( t => t.Contains( $"Package '{rUp.DefaultProjectName}' is referenced in two versions by this repository:" )
                                     && t.Contains( "Deployment/GenerateAssets.cs" ) );
        }
    }

    /// <summary>
    /// A "//ck-package" of a package of the solution itself is an error (a "#:package" is ignored), as is a
    /// "//ck-package" without version or with an invalid one (a "#:package" is skipped): nothing else can give it.
    /// </summary>
    [TestCase( "own", "is a project of this solution." )]
    [TestCase( "", "a version is required" )]
    [TestCase( "@", "the version is not a valid version." )]
    [TestCase( "@1.*", "the version is not a valid version." )]
    public async Task invalid_ck_package_directives_are_errors_Async( string suffix, string expected )
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var r = await world.CreateRepoAsync( "X-Alone", "v1.0.0" ).ConfigureAwait( false );
        WriteScriptLines( r, suffix == "own"
                                ? $"//ck-package {r.DefaultProjectName}@1.0.0"
                                : $"//ck-package Some.Package{suffix}" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeFalse();
            logs.ShouldContain( t => t.Contains( "//ck-package" ) && t.Contains( expected ) );
        }
    }

    static void WriteScript( FakeBuildRepo repo, string packageId, string version ) => WriteScriptLines( repo, $"#:package {packageId}@{version}" );

    static void WriteScriptLines( FakeBuildRepo repo, params string[] directives )
    {
        using var e = repo.CreateEditor();
        TestHelper.TouchAndCommit( e.GitRepository.Repository,
                                   "Deployment/GenerateAssets.cs",
                                   fileContent: _ => string.Concat( directives.Select( d => d + "\r\n" ) ) + "return 0;\r\n" );
    }

    static string ReadScript( FakeBuildRepo repo )
    {
        return File.ReadAllText( repo.WorkingFolderPath.Combine( "Deployment/GenerateAssets.cs" ) );
    }

    // The annotation (the BuildContentInfo) of the greatest version tag.
    static string LastTagAnnotation( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                .Select( t => (Tag: t, V: SVersion.TryParse( t.FriendlyName.Substring( t.FriendlyName.LastIndexOf( '/' ) + 1 ).TrimStart( 'v' ), out var v ) ? v : null) )
                .Where( t => t.V != null )
                .MaxBy( t => t.V )
                .Tag.Annotation.Message;
    }

    // The greatest version of the version tags ("v1.0.1--ci.2" or "building/v..."...).
    static SVersion LastVersion( FakeBuildRepo repo )
    {
        using var e = repo.CreateEditor();
        return e.GitRepository.Repository.Tags
                .Select( t => SVersion.TryParse( t.FriendlyName.Substring( t.FriendlyName.LastIndexOf( '/' ) + 1 ).TrimStart( 'v' ), out var v ) ? v : null )
                .Where( v => v != null )
                .Max()!;
    }
}
