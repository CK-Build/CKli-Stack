using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// <see cref="NuGetFeed.CreateReadClient"/>, <see cref="NuGetFeed.CreatePushClient"/> and the
/// <see cref="NuGetFeedClient"/> they produce.
/// <para>
/// These use a "file://" folder feed, so they are offline and deterministic:
/// <see cref="NuGetHelper.EnsureLocalFeed"/> seeds it with "ck.canarypackage/1.0.0" and
/// <see cref="NuGetFeedClient.GetVersionsAsync"/> reads the V3 expanded folder layout directly.
/// </para>
/// </summary>
public class NuGetFeedClientTests
{
    const string _apiKeySecretKey = "A_FEED_API_KEY";

    /// <summary>
    /// A feed with neither credentials: a true public feed. Nothing is resolved and the store is never
    /// asked anything.
    /// </summary>
    [Test]
    public async Task a_read_client_on_a_public_feed_lists_the_versions_Async()
    {
        var feed = CreateLocalFeed( "public" );
        var store = new TestSecretsStore();

        using var client = feed.CreateReadClient( TestHelper.Monitor, store ).ShouldNotBeNull();
        store.RequestedKeys.ShouldBeEmpty( "A true public feed resolves nothing." );

        // A read client never writes, whatever the feed's credentials are.
        client.CanWrite.ShouldBeFalse();

        // The package identifier is case insensitive: the folder layout is lowercase.
        var versions = await client.GetVersionsAsync( TestHelper.Monitor, "CK.CanaryPackage" ).ConfigureAwait( false );
        versions.ShouldNotBeNull().ShouldHaveSingleItem().ToString().ShouldBe( "1.0.0" );

        // An unknown package is not an error: the feed simply has no version of it.
        var none = await client.GetVersionsAsync( TestHelper.Monitor, "No.Such.Package" ).ConfigureAwait( false );
        none.ShouldNotBeNull().ShouldBeEmpty();
    }

    /// <summary>
    /// A private feed: the Credentials SecretKey is a key in the store (unlike the PublicReadCredentials,
    /// which are used as they are), so it is resolved - and a missing secret has no client.
    /// </summary>
    [Test]
    public async Task a_read_client_on_a_private_feed_resolves_its_api_key_Async()
    {
        var feed = CreateLocalFeed( "private", credentials: new NuGetFeedCredentials( _apiKeySecretKey, null ) );

        // The secret is registered: the client can read but still not write.
        var store = new TestSecretsStore( (_apiKeySecretKey, "the-api-key") );
        using( var client = feed.CreateReadClient( TestHelper.Monitor, store ).ShouldNotBeNull() )
        {
            store.RequestedKeys.ShouldContain( _apiKeySecretKey );
            client.CanWrite.ShouldBeFalse();
            var versions = await client.GetVersionsAsync( TestHelper.Monitor, "CK.CanaryPackage" ).ConfigureAwait( false );
            versions.ShouldNotBeNull().ShouldHaveSingleItem().ToString().ShouldBe( "1.0.0" );
        }

        // No secret: no client. The store's contract is to explain how to register it, and that log
        // matters as much as the null.
        using( TestHelper.Monitor.CollectEntries( out var entries ) )
        {
            feed.CreateReadClient( TestHelper.Monitor, new TestSecretsStore() ).ShouldBeNull();
            entries.ShouldContain( e => e.Text.Contains( _apiKeySecretKey ) );
        }
    }

    /// <summary>
    /// PublicReadCredentials are used as they are - both of them - so a client can be created without the
    /// store holding anything at all. This is what makes a feed that requires an authentication to read
    /// (GitHub Packages) usable by anyone who can read the repository.
    /// </summary>
    [Test]
    public async Task a_read_client_uses_the_PublicReadCredentials_as_they_are_Async()
    {
        var feed = CreateLocalFeed( "publicRead",
                                    publicReadCredentials: new NuGetFeedCredentials( "not-a-store-key", "someUser" ) );
        var store = new TestSecretsStore();

        using var client = feed.CreateReadClient( TestHelper.Monitor, store ).ShouldNotBeNull();
        store.RequestedKeys.ShouldBeEmpty( "Nothing is resolved: both values are used as they are." );

        client.CanWrite.ShouldBeFalse();
        var versions = await client.GetVersionsAsync( TestHelper.Monitor, "CK.CanaryPackage" ).ConfigureAwait( false );
        versions.ShouldNotBeNull().ShouldHaveSingleItem().ToString().ShouldBe( "1.0.0" );
    }

    /// <summary>
    /// Push and delete throw on a read client rather than failing deep inside NuGet with a null API key.
    /// </summary>
    [Test]
    public void a_read_client_refuses_to_write()
    {
        var feed = CreateLocalFeed( "readonly" );

        using var client = feed.CreateReadClient( TestHelper.Monitor, new TestSecretsStore() ).ShouldNotBeNull();

        client.CanWrite.ShouldBeFalse();
        Should.Throw<InvalidOperationException>( () => client.PushAsync( TestHelper.Monitor, "any.1.0.0.nupkg" ) );
        Should.Throw<InvalidOperationException>( () => client.DeleteAsync( TestHelper.Monitor,
                                                                          "CK.CanaryPackage",
                                                                          SVersion.Parse( "1.0.0" ) ) );
    }

    /// <summary>
    /// A push client requires the feed's Credentials: CanPush is what decides whether a given version may
    /// be pushed, but having an API key at all is what makes a client able to write.
    /// </summary>
    [Test]
    public void a_push_client_requires_the_feed_Credentials()
    {
        var withKey = CreateLocalFeed( "push", credentials: new NuGetFeedCredentials( _apiKeySecretKey, null ) );
        var store = new TestSecretsStore( (_apiKeySecretKey, "the-api-key") );

        using( var client = withKey.CreatePushClient( TestHelper.Monitor, store ).ShouldNotBeNull() )
        {
            client.CanWrite.ShouldBeTrue();
        }
        withKey.CreatePushClient( TestHelper.Monitor, new TestSecretsStore() ).ShouldBeNull( "The api key is missing." );

        // No Credentials at all: this is a programming error, not a missing secret.
        var noKey = CreateLocalFeed( "noKey" );
        Should.Throw<InvalidOperationException>( () => noKey.CreatePushClient( TestHelper.Monitor, store ) );
    }

    /// <summary>
    /// Creates a "file://" folder feed seeded with "ck.canarypackage/1.0.0".
    /// </summary>
    static NuGetFeed CreateLocalFeed( string name,
                                      NuGetFeedCredentials? credentials = null,
                                      NuGetFeedCredentials? publicReadCredentials = null )
    {
        var path = Path.GetFullPath( TestHelper.TestProjectFolder.Combine( $"FeedClient/{name}" ) );
        Directory.CreateDirectory( path );
        NuGetHelper.EnsureLocalFeed( TestHelper.Monitor, path ).ShouldBeTrue();
        return new NuGetFeed( name,
                              "file://" + path.Replace( '\\', '/' ),
                              credentials,
                              pushQualityFilter: null,
                              publicReadCredentials );
    }

    /// <summary>
    /// A store with a known set of secrets that records what was asked of it.
    /// <para>
    /// The real <see cref="DotNetUserSecretsStore"/> cannot be used here: it reads
    /// <see cref="CKliRootEnv.InstanceName"/>, which requires an initialized root environment that these
    /// tests deliberately don't need.
    /// </para>
    /// </summary>
    sealed class TestSecretsStore : ISecretsStore
    {
        readonly Dictionary<string, string> _secrets;

        public TestSecretsStore( params (string Key, string Secret)[] secrets )
        {
            _secrets = new Dictionary<string, string>();
            foreach( var (k, s) in secrets ) _secrets.Add( k, s );
        }

        public List<string> RequestedKeys { get; } = new();

        public string? TryGetRequiredSecret( IActivityMonitor monitor, IEnumerable<string> keys, LogLevel level = LogLevel.Error )
        {
            string? found = null;
            foreach( var k in keys )
            {
                RequestedKeys.Add( k );
                if( found == null ) _secrets.TryGetValue( k, out found );
            }
            if( found == null )
            {
                // The real store returns null AND explains how to register the secret: a silent double
                // turns a miswiring into an unexplained null.
                monitor.Log( level, $"TestSecretsStore has no secret for '{RequestedKeys.Concatenate( "', '" )}'." );
            }
            return found;
        }
    }
}
