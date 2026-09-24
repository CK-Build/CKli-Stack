using CK.Core;
using CKli.VersionTag.Plugin;
using NUnit.Framework;
using Shouldly;

namespace Plugins.Tests;

/// <summary>
/// The "Debug" or "Release" build configuration is a function of the version to build
/// (<see cref="CommitBuildInfo.IsReleaseConfiguration(SVersion)"/>).
/// </summary>
public class BuildConfigurationTests
{
    /// <summary>
    /// Stable versions, "-romeo" to "-zulu" prereleases and exploratory versions are built in "Release",
    /// "-alpha" to "-quebec" prereleases in "Debug".
    /// </summary>
    [TestCase( "1.2.3", true )]
    [TestCase( "1.2.3-zulu", true )]
    [TestCase( "1.2.3-romeo.2", true )]
    [TestCase( "0.0.0-0.explo", true )]
    [TestCase( "1.2.3-quebec", false )]
    [TestCase( "1.2.3-papa.1", false )]
    [TestCase( "1.2.3-alpha", false )]
    public void regular_versions_configuration( string version, bool release )
    {
        var v = SVersion.Parse( version );
        v.IsCI.ShouldBeFalse();
        CommitBuildInfo.IsReleaseConfiguration( v ).ShouldBe( release );
    }

    /// <summary>
    /// A CI version is always built in "Debug", whatever the kind of version it follows: a CI build
    /// of a stable, "-romeo" to "-zulu" or exploratory branch used to be built (and tested) in "Release".
    /// </summary>
    [TestCase( "1.2.4--ci.5" )]
    [TestCase( "1.2.3-zulu.0.ci.3" )]
    [TestCase( "1.2.3-romeo.0.ci.1" )]
    [TestCase( "0.0.0-0.explo.0.ci.1" )]
    [TestCase( "1.2.3-alpha.0.ci.42" )]
    public void CI_versions_are_always_built_in_Debug( string version )
    {
        var v = SVersion.Parse( version );
        v.IsCI.ShouldBeTrue();
        v.VersionKind.ShouldNotBe( CSVersionKind.None );
        CommitBuildInfo.IsReleaseConfiguration( v ).ShouldBeFalse();
    }
}
