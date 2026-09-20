using CK.Core;
using CK.Monitoring;
using CKli;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The <c>IPackageMapping</c> that "ckli build" applies to the package references of the solutions it builds:
/// <c>Roadmap.PackageMapping</c>, handed to <c>MutableSolution.UpdatePackages</c>.
/// <para>
/// That mapping is a composite of three: the packages this World produces, the
/// &lt;VersionTag&gt;&lt;Packages&gt; bounds and the discrepancies between the repositories. Its
/// <c>HasMapping</c> answers "this composite covers the identifier" - it is what decides whether a
/// &lt;PackageReference&gt; is looked at at all - while <c>GetMappedVersion</c> answers "and this is the
/// version it must carry". The two part company on a reference that is already where it belongs, and that
/// is what this fixture is about.
/// </para>
/// </summary>
public class PackageMappingTests
{
    /// <summary>
    /// A reference that already satisfies its &lt;Package&gt; bound is left alone - and SILENTLY so.
    /// <para>
    /// The bound covers the identifier, so <c>HasMapping</c> is true and <c>MutableSolution.UpdateVersions</c>
    /// reads the version; the version is in the bound, so the bound has nothing to say about it and
    /// <c>GetMappedVersion</c> answers null. That null means "nothing to change here", NOT the "unhandled
    /// version" that the warning reports.
    /// </para>
    /// <para>
    /// This is the CK World's own <c>&lt;Package Name="Microsoft.AspNetCore.*" Version="8.0.0[LockMajor]" /&gt;</c>:
    /// every build warned about every in-bound reference of the family.
    /// </para>
    /// </summary>
    [Test]
    public async Task an_in_bound_reference_is_left_alone_without_a_warning_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: BoundsConfiguration( ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]") ) )
                                 .ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rCore = await world.CreateRepoAsync( "X-Core", "v1.0.1" ).ConfigureAwait( false );
        using( var e = rCore.CreateEditor() )
        {
            // In its bound: a 8.* is what "8.0.0[LockMajor]" asks for, the build has nothing to do about it.
            e.AddOrUpdateReference( rCore.DefaultProjectName, "Microsoft.AspNetCore.Authentication.OpenIdConnect", SVersion.Parse( "8.0.31" ) );
            // Out of its bound: the build brings it back to the bound's base version.
            e.AddOrUpdateReference( rCore.DefaultProjectName, "Microsoft.AspNetCore.Http", SVersion.Parse( "7.0.1" ) );
        }

        // A GrandOutput memory collector, not TestHelper.Monitor.CollectTexts: the roadmap updates the
        // dependencies on its own per solution monitor (builds go up to --max-dop), which a monitor scoped
        // client never sees. ExtractCurrentTexts() waits for the dispatcher before returning.
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
            var texts = logs.ExtractCurrentTexts();
            texts.ShouldNotContain( t => t.Contains( "Unhandled version" ),
                                    customMessage: texts.Concatenate( Environment.NewLine ) );
        }
        // A non CI build integrates "dev/stable" and commits its "Updated dependencies." on "stable".
        Reference( rCore, "stable", "Microsoft.AspNetCore.Authentication.OpenIdConnect" )
            .ShouldBe( "8.0.31", "In its bound: untouched." );
        Reference( rCore, "stable", "Microsoft.AspNetCore.Http" )
            .ShouldBe( "8.0.0", "Out of its bound: back to the bound's base version." );
    }

    // Configures the fake feeds and the <VersionTag><Packages> bounds, in this order - which is their priority
    // order. A Name that holds a '*' covers the family of every identifier it matches.
    static Action<IActivityMonitor, NormalizedPath, XElement> BoundsConfiguration( params (string Name, string Bound)[] bounds )
    {
        return ( monitor, stackPath, plugins ) =>
        {
            Helper.ConfigureFakeFeeds( monitor, stackPath, plugins );
            var versionTag = plugins.Element( "VersionTag" ).ShouldNotBeNull();
            versionTag.Add( new XElement( "Packages",
                                bounds.Select( b => new XElement( "Package",
                                                        new XAttribute( "Name", b.Name ),
                                                        new XAttribute( "Version", b.Bound ) ) ) ) );
        };
    }

    static string? Reference( FakeBuildRepo repo, string branchName, string packageId )
    {
        using var e = repo.CreateEditor();
        var p = e.ReadProjects( branchName ).Single( x => x.ProjectName == repo.DefaultProjectName );
        var found = p.References.Where( x => x.PackageId == packageId ).ToList();
        found.Count.ShouldBeLessThanOrEqualTo( 1, $"'{packageId}' is referenced more than once." );
        return found.Count == 0 ? null : found[0].Version.ToString();
    }
}
