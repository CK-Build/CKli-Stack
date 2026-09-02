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
    /// in the <see cref="DotNetUserSecretsStore"/> (that is used by these tests).
    /// </summary>
    /// <returns></returns>
    public static GitHostingProvider GetGitHubHostingProvider()
    {
        if( _gitHubProvider == null )
        {
            // Using the real store here: the PAT must be locally registered for these tests to run.
            var store = new DotNetUserSecretsStore();
            // The CKli repository is public.
            // The hosting provider is public (new repositories will be public by default).
            // But, to call GitHub, it is better to always use a PAT because anonymous API calls
            // have a low rate limit: AlwaysUseAuthentication is true for GitHub.
            // => To challenge the Read credentials, we must actually consider the ToPrivateAccessKey() instance.
            var gitKey = new GitRepositoryKey( store, new Uri( "https://github.com/CK-Build/CKli" ), isPublic: true );
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

    public static void ConfigureFakeFeeds( IActivityMonitor monitor, NormalizedPath stackPath, XElement plugins )
    {
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
            var key = f.Element( "PushCredentials" )?.Attribute( "SecretKey" );
            key.ShouldNotBeNull().SetValue( "FILESYSTEM_GIT" );
        }
    }
}
