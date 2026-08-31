using CK.Core;
using CKli.Core;
using Shouldly;
using System;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

public static class Helper
{
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
