using CK.Core;
using CKli.Core;
using CKli.Core.GitHosting.Providers;
using NUnit.Framework;
using Shouldly;
using System;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;


internal static class Helper
{
    static HttpGitHostingProvider? _gitHubProvider;

    /// <summary>
    /// Gets the GitHub provider through the https://github.com/CK-Build/CKli repository itself and caches it.
    /// This fails (<see cref="Assume.That(bool, FormattableString, string)"/>) if the GITHUB_CK_BUILD is not available
    /// in this <see cref="CKliRootEnv.InstanceName"/> "CKli-CKli-Test" user secrets store: this uses the central <see cref="CKliRootEnv.SecretsStore"/>.
    /// </summary>
    /// <returns></returns>
    public static GitHostingProvider GetGitHubHostingProvider()
    {
        if( _gitHubProvider == null )
        {
            // The CKli repository is public.
            // The hosting provider is public (new repositories will be public by default).
            // But, to call GitHub, it is better to always use a PAT because anonymous API calls
            // have a low rate limit: AlwaysUseAuthentication is true for GitHub.
            // => To challenge the Read credentials, we must actually consider the ToPrivateAccessKey() instance.
            var gitKey = new GitRepositoryKey( CKliRootEnv.SecretsStore, new Uri( "https://github.com/CK-Build/CKli" ), isPublic: true );
            gitKey.AccessKey.PrefixPAT.ShouldBe( "GITHUB_CK_BUILD" );
            _gitHubProvider = (HttpGitHostingProvider?)gitKey.AccessKey.HostingProvider;
            _gitHubProvider.ShouldNotBeNull();
            _gitHubProvider.AlwaysUseAuthentication.ShouldBeTrue();
            Assume.That( _gitHubProvider.GitKey.ToPrivateAccessKey().GetReadCredentials( TestHelper.Monitor, out var _ ),
                         "The user-secrets store must be configured." );
        }
        return _gitHubProvider;
    }

    public static (NormalizedPath NuGetOrgPath, NormalizedPath SignatureOSPath) GetFakeFeedPaths( NormalizedPath clonedFolder )
    {
        return (clonedFolder.Combine( "FakeFeed/nuget.org" ), clonedFolder.Combine( "FakeFeed/Signature-OpenSource" ));
    }

    /// <summary>
    /// Removes the &lt;VersionTag&gt;&lt;Packages&gt; bounds that a test clone inherits from the World this suite
    /// runs in: <c>RemotesCollection.CloneAsync</c> writes that World's &lt;Plugins&gt; element into every clone,
    /// so a bound declared for the CKli stack itself would otherwise apply to every fixture and decide what they
    /// build - the CKt(...) fixtures pin packages that such a bound moves.
    /// <para>
    /// A fixture states the bounds it is about, and inherits none. <see cref="ConfigureFakeFeeds"/> calls this,
    /// so it is only passed explicitly by the fixtures that configure no feed.
    /// </para>
    /// </summary>
    public static void RemoveAmbientPackageBounds( IActivityMonitor monitor, NormalizedPath stackPath, XElement plugins )
    {
        plugins.Elements( "VersionTag" ).Elements( "Packages" ).Remove();
    }

    public static void ConfigureFakeFeeds( IActivityMonitor monitor, NormalizedPath stackPath, XElement plugins )
    {
        RemoveAmbientPackageBounds( monitor, stackPath, plugins );
        var (nugetOrgFeed, sosFeed) = GetFakeFeedPaths( stackPath.RemoveLastPart() );
        NuGetHelper.EnsureLocalFeed( monitor, nugetOrgFeed );
        NuGetHelper.EnsureLocalFeed( monitor, sosFeed );
        foreach( var f in plugins.Elements( "ArtifactHandler" ).Elements( "NuGet" ).Elements( "Feed" ) )
        {
            var url = f.Attribute( "Url" ).ShouldNotBeNull();
            url.SetValue( url.Value switch
            {
                "https://api.nuget.org/v3/index.json" => $"file://{nugetOrgFeed}",
                "https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json" => $"file://{sosFeed}",
                _ => Throw.NotSupportedException<string>( url.Value )
            } );
            var key = f.Element( "Credentials" )?.Attribute( "SecretKey" );
            key.ShouldNotBeNull().SetValue( "FILESYSTEM_GIT" );
        }
    }
}
