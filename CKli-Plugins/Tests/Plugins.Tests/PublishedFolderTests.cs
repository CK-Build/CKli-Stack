using CK.Core;
using CK.Packaging.Abstractions;
using CKli.Publish.Plugin;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// <see cref="PublishedFolder"/> unit tests: no World is involved, the folder is a plain
/// directory of Json <see cref="PublishedProfile"/> files.
/// </summary>
[TestFixture]
public class PublishedFolderTests
{
    // Each test works in its own folder: TestHelper.CleanupFolder creates an empty one.
    static string GetCleanFolder( string testName )
    {
        var p = Path.GetFullPath( Path.Combine( Path.GetTempPath(), "Plugins.Tests.PublishedFolder", testName ) );
        TestHelper.CleanupFolder( p, ensureFolderAvailable: true );
        return p;
    }

    // Builds the profiles of the "CKt" test stack.
    static class TestModel
    {
        public static readonly Uri StackUrl = new Uri( "https://github.com/Signature-Code/CKt-Stack" );

        public static readonly WorldName World = WorldName.Parse( "CKt" );

        /// <summary>
        /// Parses a Conformant SVersion (a package version).
        /// </summary>
        public static SVersion V( string version ) => SVersion.Parse( version, mustBeCSVersion: true );

        /// <summary>
        /// Parses a <see cref="TimeVersion"/> (a profile version).
        /// </summary>
        public static TimeVersion TV( string version ) => TimeVersion.Parse( version );

        /// <summary>
        /// Creates a profile of 2 repositories and 3 packages, all in the provided package version.
        /// </summary>
        /// <param name="version">The profile version.</param>
        /// <param name="packageVersion">The version of the packages.</param>
        public static PublishedProfile SampleProfile( string version, string packageVersion = "1.2.3" )
        {
            return new PublishedProfile( StackUrl,
                                         World,
                                         TV( version ),
                                         [Repo( "Two", 2, $"CK.Two@{packageVersion}" ),
                                          Repo( "One", 1, $"CK.One.Sub@{packageVersion}", $"CK.One@{packageVersion}" )] );
        }

        static Repository Repo( string name, ulong id, params string[] packages )
        {
            var key = new RepositoryKey( new Uri( $"https://github.com/Signature-Code/CKt-{name}" ),
                                         new RandomId( id ) );
            return new Repository( key, packages.Select( Package ).ToImmutableArray() );
        }

        static PackageInstance Package( string packageInstance )
        {
            return PackageInstance.TryParse( packageInstance, out var p )
                    ? p
                    : throw new ArgumentException( $"Invalid package instance '{packageInstance}'." );
        }
    }

    [Test]
    public void the_root_directory_must_exist_unless_createIfMissing()
    {
        var root = GetCleanFolder( nameof( the_root_directory_must_exist_unless_createIfMissing ) );
        var missing = Path.Combine( root, "Published" );

        Should.Throw<ArgumentException>( () => new PublishedFolder( missing ) )
              .Message.ShouldStartWith( $"Published folder directory must exist: '{missing}'." );

        var f = new PublishedFolder( missing, createIfMissing: true );
        Directory.Exists( missing ).ShouldBeTrue();
        f.RootPath.ShouldBe( missing + Path.DirectorySeparatorChar );
    }

    [Test]
    public void an_empty_folder_has_no_profile()
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( an_empty_folder_has_no_profile ) ) );

        f.Profiles.ShouldBeEmpty();
        f.LoadErrors.ShouldBeEmpty();
        f.IsDirty.ShouldBeFalse();
        f.Save().ShouldBe( 0 );
        f.Find( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
        f.GetLoadError( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
    }

    [TestCase( "2026.11.20", @"v2026.11.20.json" )]
    [TestCase( "2026.11.20.1--ci.5", @"v2026.11.20.1--ci.5.json" )]
    [TestCase( "2026.11.21-alpha", @"alpha\v2026.11.21-alpha.json" )]
    [TestCase( "2026.11.21-zulu.0.ci.12", @"zulu\v2026.11.21-zulu.0.ci.12.json" )]
    [TestCase( "2026.11.21-0.some-explo", @"explo\some-explo\v2026.11.21-0.some-explo.json" )]
    public void the_file_path_of_a_profile_follows_its_branch_name( string version, string relativePath )
    {
        var root = GetCleanFolder( nameof( the_file_path_of_a_profile_follows_its_branch_name ) );
        var f = new PublishedFolder( root );

        var expected = Path.Combine( root, relativePath.Replace( '\\', Path.DirectorySeparatorChar ) );
        f.GetProfileFilePath( TestModel.TV( version ) ).ShouldBe( expected );
    }

    [Test]
    public void added_profiles_are_written_by_Save_and_read_back()
    {
        var root = GetCleanFolder( nameof( added_profiles_are_written_by_Save_and_read_back ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "2026.11.21-alpha", "1.3.0-alpha" ) );
        f.Add( TestModel.SampleProfile( "2026.11.21-0.some-explo", "0.0.0-0.some-explo" ) );
        f.IsDirty.ShouldBeTrue();
        // Nothing is written before Save.
        Directory.EnumerateFiles( root, "*.json", SearchOption.AllDirectories ).ShouldBeEmpty();

        f.Save().ShouldBe( 3 );
        f.IsDirty.ShouldBeFalse();
        f.Save().ShouldBe( 0, "Nothing more to do." );

        File.Exists( Path.Combine( root, "v2026.11.20.json" ) ).ShouldBeTrue();
        File.Exists( Path.Combine( root, "alpha", "v2026.11.21-alpha.json" ) ).ShouldBeTrue();
        File.Exists( Path.Combine( root, "explo", "some-explo", "v2026.11.21-0.some-explo.json" ) ).ShouldBeTrue();

        // A brand new folder reads the files.
        var f2 = new PublishedFolder( root );
        f2.Profiles.Select( p => p.Version.ToString() )
                   .ShouldBe( new[] { "2026.11.21-alpha", "2026.11.21-0.some-explo", "2026.11.20" },
                              "Ordered by descending version." );
        f2.LoadErrors.ShouldBeEmpty();

        var found = f2.Find( TestModel.TV( "2026.11.20" ) );
        found.ShouldNotBeNull();
        found.ToJsonString().ShouldBe( TestModel.SampleProfile( "2026.11.20", "1.2.3" ).ToJsonString() );
        // The file is the indented Json.
        File.ReadAllText( Path.Combine( root, "v2026.11.20.json" ) ).ShouldBe( found.ToJsonString() );
    }

    [Test]
    public void Add_throws_when_the_version_already_exists()
    {
        var root = GetCleanFolder( nameof( Add_throws_when_the_version_already_exists ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        Should.Throw<InvalidOperationException>( () => f.Add( TestModel.SampleProfile( "2026.11.20" ) ) )
              .Message.ShouldBe( "Profile '2026.11.20' already exists." );

        f.Save().ShouldBe( 1 );

        // The saved profile is found by a new folder: Add still throws.
        var f2 = new PublishedFolder( root );
        Should.Throw<InvalidOperationException>( () => f2.Add( TestModel.SampleProfile( "2026.11.20" ) ) );
    }

    [Test]
    public void Remove_deletes_the_file()
    {
        var root = GetCleanFolder( nameof( Remove_deletes_the_file ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        f.Add( TestModel.SampleProfile( "2026.11.21-alpha" ) );
        f.Save().ShouldBe( 2 );

        f.Remove( TestModel.TV( "2026.11.20.1" ) ).ShouldBeFalse( "Unknown version." );
        f.Remove( TestModel.TV( "2026.11.20" ) ).ShouldBeTrue();
        f.Remove( TestModel.TV( "2026.11.20" ) ).ShouldBeFalse( "Already removed." );
        f.Find( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
        File.Exists( Path.Combine( root, "v2026.11.20.json" ) ).ShouldBeTrue( "Not saved yet." );

        f.Save().ShouldBe( 1 );
        File.Exists( Path.Combine( root, "v2026.11.20.json" ) ).ShouldBeFalse();

        new PublishedFolder( root ).Profiles.Select( p => p.Version.ToString() )
                                            .ShouldBe( new[] { "2026.11.21-alpha" } );

        // A removed profile can be added again.
        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        f.Save().ShouldBe( 1 );
        File.Exists( Path.Combine( root, "v2026.11.20.json" ) ).ShouldBeTrue();
    }

    [Test]
    public void Reload_forgets_the_pending_modifications()
    {
        var root = GetCleanFolder( nameof( Reload_forgets_the_pending_modifications ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        f.Save().ShouldBe( 1 );

        f.Remove( TestModel.TV( "2026.11.20" ) ).ShouldBeTrue();
        f.Add( TestModel.SampleProfile( "2026.11.22" ) );
        f.IsDirty.ShouldBeTrue();

        f.Reload();
        f.IsDirty.ShouldBeFalse();
        f.Find( TestModel.TV( "2026.11.20" ) ).ShouldNotBeNull();
        f.Find( TestModel.TV( "2026.11.22" ) ).ShouldBeNull();
    }

    [Test]
    public void Deprecate_updates_the_file()
    {
        var root = GetCleanFolder( nameof( Deprecate_updates_the_file ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        f.Save().ShouldBe( 1 );

        f.Deprecate( TestModel.TV( "2026.12.31.9" ) ).ShouldBeFalse( "Unknown version." );
        f.Deprecate( TestModel.TV( "2026.11.20" ) ).ShouldBeTrue();
        f.Deprecate( TestModel.TV( "2026.11.20" ) ).ShouldBeFalse( "Idempotent." );
        f.Find( TestModel.TV( "2026.11.20" ) )!.IsDeprecated.ShouldBeTrue();

        f.Save().ShouldBe( 1 );
        new PublishedFolder( root ).Find( TestModel.TV( "2026.11.20" ) )!.IsDeprecated.ShouldBeTrue();
    }

    [Test]
    public void OnDeprecatedPackage_deprecates_every_profile_that_carries_the_package()
    {
        var root = GetCleanFolder( nameof( OnDeprecatedPackage_deprecates_every_profile_that_carries_the_package ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "2026.11.20.1", "1.2.4" ) );
        f.Add( TestModel.SampleProfile( "2026.11.21-alpha", "1.3.0-alpha" ) );
        f.Save().ShouldBe( 3 );

        // A brand new folder: OnDeprecatedPackage reads every file.
        var f2 = new PublishedFolder( root );
        f2.OnDeprecatedPackage( "CK.Unknown", TestModel.V( "1.2.3" ) ).ShouldBeFalse();
        f2.OnDeprecatedPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f2.OnDeprecatedPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeFalse( "Already deprecated." );

        f2.Profiles.Where( p => p.IsDeprecated )
                   .Select( p => p.Version.ToString() )
                   .ShouldBe( new[] { "2026.11.20" }, "Only the profile that carries CK.One@1.2.3." );

        f2.Save().ShouldBe( 1 );
        new PublishedFolder( root ).Find( TestModel.TV( "2026.11.20" ) )!.IsDeprecated.ShouldBeTrue();
    }

    // "CK.One" of the SampleProfile has been fixed: its version becomes the provided one.
    static Dictionary<PackageInstance, SVersion> Fixed( string fixedVersion, string supersedingVersion )
    {
        return new Dictionary<PackageInstance, SVersion>
        {
            { new PackageInstance( "CK.One", TestModel.V( fixedVersion ) ), TestModel.V( supersedingVersion ) }
        };
    }

    [Test]
    public void OnFixedPackages_supersedes_the_profiles_that_carry_a_fixed_package()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_supersedes_the_profiles_that_carry_a_fixed_package ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "2026.11.20.1", "1.2.4" ) );
        f.Add( TestModel.SampleProfile( "2026.11.21-alpha", "1.3.0-alpha" ) );
        f.Save().ShouldBe( 3 );

        // A brand new folder: OnFixedPackages reads every file.
        var f2 = new PublishedFolder( root );
        var created = f2.OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) );

        // Only the profile that carries CK.One@1.2.3 is superseded. Its successor keeps its Date and takes
        // the next free ClashNumber: "2026.11.20.1" is taken, so "2026.11.20.2".
        created.Length.ShouldBe( 1 );
        var p = created[0];
        p.Version.ToString().ShouldBe( "2026.11.20.2" );
        p.IsDeprecated.ShouldBeFalse();
        p.StackUrl.ShouldBe( TestModel.StackUrl );
        p.World.FullName.ShouldBe( TestModel.World.FullName );

        // Only the fixed package moves: the produced packages are otherwise the ones it supersedes.
        p.ProducedPackages["CK.One"].Version.ToString().ShouldBe( "1.2.9" );
        p.ProducedPackages["CK.One.Sub"].Version.ToString().ShouldBe( "1.2.3" );
        p.ProducedPackages["CK.Two"].Version.ToString().ShouldBe( "1.2.3" );

        // The superseded profile records what was published and is left untouched.
        f2.Find( TestModel.TV( "2026.11.20" ) )!.ProducedPackages["CK.One"].Version.ToString().ShouldBe( "1.2.3" );

        f2.Save().ShouldBe( 1 );
        new PublishedFolder( root ).Profiles
                                   .Select( x => x.Version.ToString() )
                                   .ShouldBe( new[] { "2026.11.21-alpha", "2026.11.20.2", "2026.11.20.1", "2026.11.20" } );
    }

    [Test]
    public void a_superseding_profile_carries_the_current_feeds_not_its_origin_ones()
    {
        var root = GetCleanFolder( nameof( a_superseding_profile_carries_the_current_feeds_not_its_origin_ones ) );
        var f = new PublishedFolder( root );
        var origin = TestModel.SampleProfile( "2026.11.20", "1.2.3" );
        f.Add( new PublishedProfile( origin.StackUrl,
                                     origin.World,
                                     origin.Version,
                                     origin.Repositories,
                                     feeds: [new PublishedFeed( "Old", "https://old.example/v3/index.json" )] ) );
        f.Save().ShouldBe( 1 );

        // The fixed packages are pushed now, to the World's current feeds.
        var created = new PublishedFolder( root ).OnFixedPackages( Fixed( "1.2.3", "1.2.9" ),
                                                                   [new PublishedFeed( "New", "https://new.example/v3/index.json" )] );
        created.Length.ShouldBe( 1 );
        created[0].Feeds.Select( x => x.Name ).ShouldBe( ["New"] );
    }

    [Test]
    public void a_superseding_profile_stays_on_its_branch()
    {
        var root = GetCleanFolder( nameof( a_superseding_profile_stays_on_its_branch ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.21-alpha", "1.3.0-alpha" ) );
        f.Save().ShouldBe( 1 );

        var created = new PublishedFolder( root ).OnFixedPackages( Fixed( "1.3.0-alpha", "1.3.1" ) );
        created.Length.ShouldBe( 1 );
        created[0].Version.ToString().ShouldBe( "2026.11.21.1-alpha" );
    }

    [Test]
    public void OnFixedPackages_leaves_a_deprecated_profile_locked()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_leaves_a_deprecated_profile_locked ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Deprecate( TestModel.TV( "2026.11.20" ) ).ShouldBeTrue();
        f.Save().ShouldBe( 1 );

        new PublishedFolder( root ).OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) )
                                   .ShouldBeEmpty( "A deprecated profile is locked." );
    }

    [Test]
    public void OnFixedPackages_is_idempotent()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_is_idempotent ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Save().ShouldBe( 1 );

        var f2 = new PublishedFolder( root );
        f2.OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) ).Length.ShouldBe( 1 );
        f2.OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) )
          .ShouldBeEmpty( "The superseding profile already carries those produced packages." );
        f2.Save().ShouldBe( 1 );

        // And across a reload: retrying an interrupted fix publication adds nothing.
        new PublishedFolder( root ).OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) ).ShouldBeEmpty();
    }

    [Test]
    public void OnFixedPackages_ignores_a_package_no_profile_carries()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_ignores_a_package_no_profile_carries ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Save().ShouldBe( 1 );

        var f2 = new PublishedFolder( root );
        f2.OnFixedPackages( Fixed( "9.9.9", "9.9.10" ) ).ShouldBeEmpty( "No profile carries CK.One@9.9.9." );
        f2.OnFixedPackages( new Dictionary<PackageInstance, SVersion>() ).ShouldBeEmpty();
        f2.IsDirty.ShouldBeFalse();
    }

    [Test]
    public void OnExpiredPackage_removes_every_profile_that_carries_the_package()
    {
        var root = GetCleanFolder( nameof( OnExpiredPackage_removes_every_profile_that_carries_the_package ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "2026.11.20.1", "1.2.4" ) );
        f.Add( TestModel.SampleProfile( "2026.11.21-alpha", "1.3.0-alpha" ) );
        f.Save().ShouldBe( 3 );

        // A brand new folder: OnExpiredPackage reads every file.
        var f2 = new PublishedFolder( root );
        f2.OnExpiredPackage( "CK.Unknown", TestModel.V( "1.2.3" ) ).ShouldBeFalse();
        f2.OnExpiredPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f2.OnExpiredPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeFalse( "Already removed." );

        f2.Profiles.Select( p => p.Version.ToString() )
                   .ShouldBe( new[] { "2026.11.21-alpha", "2026.11.20.1" }, "The profile that carries CK.One@1.2.3 is gone." );

        f2.Save().ShouldBe( 1 );
        File.Exists( Path.Combine( root, "v2026.11.20.json" ) ).ShouldBeFalse();
        // An expired profile is deleted, not deprecated: nothing is left to read.
        var f3 = new PublishedFolder( root );
        f3.Find( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
        f3.GetLoadError( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
    }

    [Test]
    public void OnExpiredPackage_ignores_a_profile_that_carries_another_version()
    {
        var root = GetCleanFolder( nameof( OnExpiredPackage_ignores_a_profile_that_carries_another_version ) );
        var f = new PublishedFolder( root );
        // Only "2026.11.20" carries CK.One@1.2.3.
        f.Add( TestModel.SampleProfile( "2026.11.20", "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "2026.11.20.1", "1.2.4" ) );
        f.Save().ShouldBe( 2 );

        new PublishedFolder( root ).OnExpiredPackage( "CK.One", TestModel.V( "9.9.9" ) ).ShouldBeFalse();
        new PublishedFolder( root ).Profiles.Count().ShouldBe( 2 );
    }

    [Test]
    public void an_unreadable_file_is_a_load_error_and_can_be_replaced()
    {
        var root = GetCleanFolder( nameof( an_unreadable_file_is_a_load_error_and_can_be_replaced ) );
        File.WriteAllText( Path.Combine( root, "v2026.11.20.json" ), "{ this is not json" );
        File.WriteAllText( Path.Combine( root, "v2026.11.20.1.json" ),
                           TestModel.SampleProfile( "2026.11.20" ).ToJsonString() );

        var f = new PublishedFolder( root );

        f.Find( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
        // A syntax error is a JsonReaderException (a specialized JsonException).
        f.GetLoadError( TestModel.TV( "2026.11.20" ) ).ShouldBeAssignableTo<JsonException>();
        // The file name and the profile's version must agree.
        f.GetLoadError( TestModel.TV( "2026.11.20.1" ) )
         .ShouldBeAssignableTo<JsonException>()!
         .Message.ShouldEndWith( "contains the version '2026.11.20'." );

        f.LoadErrors.Select( e => e.Version.ToString() ).ShouldBe( new[] { "2026.11.20.1", "2026.11.20" } );
        f.Profiles.ShouldBeEmpty();

        // An invalid file doesn't prevent the add: Save replaces it.
        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        f.Save().ShouldBe( 1 );

        var f2 = new PublishedFolder( root );
        f2.Find( TestModel.TV( "2026.11.20" ) ).ShouldNotBeNull();
        f2.GetLoadError( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
    }

    [Test]
    public void a_missing_file_is_not_a_load_error()
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( a_missing_file_is_not_a_load_error ) ) );

        f.Find( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
        f.GetLoadError( TestModel.TV( "2026.11.20" ) ).ShouldBeNull();
    }

    [Test]
    public void files_that_are_not_at_their_canonical_path_are_ignored()
    {
        var root = GetCleanFolder( nameof( files_that_are_not_at_their_canonical_path_are_ignored ) );
        var good = TestModel.SampleProfile( "2026.11.20" ).ToJsonString();

        // Not a version.
        File.WriteAllText( Path.Combine( root, "index.json" ), good );
        // A version with a prefix.
        File.WriteAllText( Path.Combine( root, "profile-v2026.11.20.json" ), good );
        // A stable version in a branch folder.
        Directory.CreateDirectory( Path.Combine( root, "alpha" ) );
        File.WriteAllText( Path.Combine( root, "alpha", "v2026.11.20.json" ), good );
        // A prerelease at the root.
        File.WriteAllText( Path.Combine( root, "v2026.11.21-alpha.json" ),
                           TestModel.SampleProfile( "2026.11.21-alpha" ).ToJsonString() );
        // A version that is not a profile version (not a date).
        File.WriteAllText( Path.Combine( root, "v2026.11.31.json" ), good );
        // A 0 clash number is not a fourth part: one file name per version.
        File.WriteAllText( Path.Combine( root, "v2026.11.20.0.json" ), good );
        // A legacy "Year.DayOfYear.Number" in a branch folder.
        File.WriteAllText( Path.Combine( root, "alpha", "v2026.324.0.json" ), good );
        // The only canonical file.
        File.WriteAllText( Path.Combine( root, "v2026.11.22.json" ),
                           TestModel.SampleProfile( "2026.11.22" ).ToJsonString() );

        var f = new PublishedFolder( root );

        f.Profiles.Select( p => p.Version.ToString() ).ShouldBe( new[] { "2026.11.22" } );
        f.LoadErrors.ShouldBeEmpty();
    }

    // The profile files written before the TimeVersion are named after a "Year.DayOfYear.Number" version and
    // contain it: 2026.325.N is 2026.11.21.N.
    static void WriteLegacyProfile( string root, string version, string legacyVersion )
    {
        var json = TestModel.SampleProfile( version ).ToJsonString()
                            .Replace( $"\"Version\": \"{version}\"", $"\"Version\": \"{legacyVersion}\"" );
        json.ShouldContain( legacyVersion );
        var v = TestModel.TV( version );
        var folder = v.BranchName.Length == 0 ? root : Path.Combine( root, v.BranchName );
        Directory.CreateDirectory( folder );
        File.WriteAllText( Path.Combine( folder, $"v{legacyVersion}.json" ), json );
    }

    [Test]
    public void legacy_profile_files_are_read_and_moved_by_Save()
    {
        var root = GetCleanFolder( nameof( legacy_profile_files_are_read_and_moved_by_Save ) );
        WriteLegacyProfile( root, "2026.11.21", "2026.325.0" );
        WriteLegacyProfile( root, "2026.11.21.1-alpha", "2026.325.1-alpha" );

        // A lookup reaches a legacy file without reading the whole folder.
        new PublishedFolder( root ).Find( TestModel.TV( "2026.11.21.1-alpha" ) ).ShouldNotBeNull();

        var f = new PublishedFolder( root );
        f.Profiles.Select( p => p.Version.ToString() ).ShouldBe( ["2026.11.21.1-alpha", "2026.11.21"] );
        f.LoadErrors.ShouldBeEmpty();
        // Moving a file is not a change of a profile.
        f.IsDirty.ShouldBeFalse();
        // The legacy profiles use their ClashNumber.
        f.CreateNewProfileVersion( CSVersionKind.Stable, utcNow: new DateTime( 2026, 11, 21, 8, 0, 0, DateTimeKind.Utc ) )
         .ToString().ShouldBe( "2026.11.21.1" );

        f.Save().ShouldBe( 0 );
        File.Exists( Path.Combine( root, "v2026.325.0.json" ) ).ShouldBeFalse();
        File.Exists( Path.Combine( root, "alpha", "v2026.325.1-alpha.json" ) ).ShouldBeFalse();
        File.ReadAllText( Path.Combine( root, "v2026.11.21.json" ) )
            .ShouldBe( TestModel.SampleProfile( "2026.11.21" ).ToJsonString() );
        File.ReadAllText( Path.Combine( root, "alpha", "v2026.11.21.1-alpha.json" ) )
            .ShouldBe( TestModel.SampleProfile( "2026.11.21.1-alpha" ).ToJsonString() );
        // The index names the moved files.
        File.ReadAllBytes( f.IndexFilePath ).ShouldBe( f.CreateIndexUtf8Bytes() );
        ReadIndex( f ).ShouldContain( "\"2026.11.21.1-alpha\"" );

        f.Reload();
        f.Profiles.Select( p => p.Version.ToString() ).ShouldBe( ["2026.11.21.1-alpha", "2026.11.21"] );
        f.Save().ShouldBe( 0 );
    }

    [Test]
    public void legacy_profile_files_can_be_changed_and_removed()
    {
        var root = GetCleanFolder( nameof( legacy_profile_files_can_be_changed_and_removed ) );
        WriteLegacyProfile( root, "2026.11.21", "2026.325.0" );
        WriteLegacyProfile( root, "2026.11.21.1", "2026.325.1" );

        var f = new PublishedFolder( root );
        f.Deprecate( TestModel.TV( "2026.11.21" ) ).ShouldBeTrue();
        f.Remove( TestModel.TV( "2026.11.21.1" ) ).ShouldBeTrue();
        f.Save().ShouldBe( 2 );

        Directory.EnumerateFiles( root, "*.json" ).Select( Path.GetFileName )
                 .Order().ShouldBe( ["index.json", "v2026.11.21.json"] );
        new PublishedFolder( root ).Find( TestModel.TV( "2026.11.21" ) )!.IsDeprecated.ShouldBeTrue();
    }

    [Test]
    public void RebuildIndex_moves_the_legacy_profile_files()
    {
        var root = GetCleanFolder( nameof( RebuildIndex_moves_the_legacy_profile_files ) );
        WriteLegacyProfile( root, "2026.11.21", "2026.325.0" );

        var f = new PublishedFolder( root );
        f.RebuildIndex();
        File.Exists( Path.Combine( root, "v2026.325.0.json" ) ).ShouldBeFalse();
        File.Exists( Path.Combine( root, "v2026.11.21.json" ) ).ShouldBeTrue();
        PublishedIndex.Parse( File.ReadAllBytes( f.IndexFilePath ) ).GetAlive( PublishedIndex.StableGroupName )
                      .Select( v => v.ToString() ).ShouldBe( ["2026.11.21"] );
    }

    // 2026 is not a leap year: the 254th day is the 11th of September.
    static readonly DateTime _day254 = new DateTime( 2026, 9, 11, 13, 45, 0, DateTimeKind.Utc );

    [TestCase( CSVersionKind.Stable, false, "2026.9.11" )]
    [TestCase( CSVersionKind.Stable, true, "2026.9.11--ci.0" )]
    [TestCase( CSVersionKind.Alpha, false, "2026.9.11-alpha" )]
    [TestCase( CSVersionKind.Alpha, true, "2026.9.11-alpha.0.ci.0" )]
    [TestCase( CSVersionKind.Zulu, false, "2026.9.11-zulu" )]
    [TestCase( CSVersionKind.Zulu, true, "2026.9.11-zulu.0.ci.0" )]
    public void a_new_profile_version_is_time_based( CSVersionKind branchKind, bool isCIBuild, string expected )
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( a_new_profile_version_is_time_based ) ) );

        var v = f.CreateNewProfileVersion( branchKind, isCIBuild: isCIBuild, utcNow: _day254 );
        v.ToString().ShouldBe( expected );
        // The version carries the branch and the CI build that has been published.
        v.VersionKind.ShouldBe( branchKind );
        v.IsCI.ShouldBe( isCIBuild );
    }

    [Test]
    public void a_new_profile_version_of_an_exploratory_branch_carries_its_name()
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( a_new_profile_version_of_an_exploratory_branch_carries_its_name ) ) );

        f.CreateNewProfileVersion( CSVersionKind.Exploratory, "some-explo", utcNow: _day254 )
         .ToString().ShouldBe( "2026.9.11-0.some-explo" );
        f.CreateNewProfileVersion( CSVersionKind.Exploratory, "some-explo", isCIBuild: true, utcNow: _day254 )
         .ToString().ShouldBe( "2026.9.11-0.some-explo.0.ci.0" );
    }

    [Test]
    public void a_new_profile_version_requires_a_branch_kind_that_exists()
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( a_new_profile_version_requires_a_branch_kind_that_exists ) ) );

        Should.Throw<ArgumentException>( () => f.CreateNewProfileVersion( CSVersionKind.None ) )
              .Message.ShouldStartWith( "Invalid branch kind 'None'." );
        Should.Throw<ArgumentException>( () => f.CreateNewProfileVersion( CSVersionKind.Exploratory ) )
              .Message.ShouldStartWith( "An exploratory branch requires its name." );
    }

    [Test]
    public void the_number_of_a_new_profile_version_is_minted_above_the_ones_of_its_day()
    {
        var root = GetCleanFolder( nameof( the_number_of_a_new_profile_version_is_minted_above_the_ones_of_its_day ) );
        var f = new PublishedFolder( root );

        f.CreateNewProfileVersion( CSVersionKind.Alpha, utcNow: _day254 ).ToString().ShouldBe( "2026.9.11-alpha" );
        // A pending Add is enough: the folder doesn't need to be saved.
        f.Add( TestModel.SampleProfile( "2026.9.11-alpha" ) );
        f.CreateNewProfileVersion( CSVersionKind.Alpha, utcNow: _day254 ).ToString().ShouldBe( "2026.9.11.1-alpha" );

        f.Add( TestModel.SampleProfile( "2026.9.11.1-alpha" ) );
        f.Save().ShouldBe( 2 );
        f.Reload();
        f.CreateNewProfileVersion( CSVersionKind.Alpha, utcNow: _day254 ).ToString().ShouldBe( "2026.9.11.2-alpha" );

        // Another branch has its own number sequence.
        f.CreateNewProfileVersion( CSVersionKind.Stable, utcNow: _day254 ).ToString().ShouldBe( "2026.9.11" );
        // But the CI builds of a branch SHARE its sequence (they have the same TimeVersion.BranchName): a number
        // is never reused, so a CI publication can never sort below the release it follows - and
        // RemoveSupersededCIProfiles is free to delete the ones it supersedes.
        f.CreateNewProfileVersion( CSVersionKind.Alpha, isCIBuild: true, utcNow: _day254 )
         .ToString().ShouldBe( "2026.9.11.2-alpha.0.ci.0" );
        // Another day starts again at 0.
        f.CreateNewProfileVersion( CSVersionKind.Alpha, utcNow: _day254.AddDays( 1 ) ).ToString().ShouldBe( "2026.9.12-alpha" );
    }

    [Test]
    public void a_publication_removes_the_CI_profiles_it_supersedes()
    {
        var root = GetCleanFolder( nameof( a_publication_removes_the_CI_profiles_it_supersedes ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "2026.9.11" ) );
        f.Add( TestModel.SampleProfile( "2026.9.11.1--ci.0" ) );
        f.Add( TestModel.SampleProfile( "2026.9.11.2--ci.0" ) );
        // Another branch: its CI profiles are none of this branch's business.
        f.Add( TestModel.SampleProfile( "2026.9.11.3-alpha.0.ci.0" ) );
        f.Save().ShouldBe( 4 );

        // A CI publication supersedes the CI publications that precede it on its branch, and only those.
        f.Add( TestModel.SampleProfile( "2026.9.11.4--ci.0" ) );
        f.RemoveSupersededCIProfiles( TestModel.TV( "2026.9.11.4--ci.0" ) )
         .Select( v => v.ToString() )
         .ShouldBe( ["2026.9.11.1--ci.0", "2026.9.11.2--ci.0"] );
        f.Profiles.Select( p => p.Version.ToString() )
         .ShouldBe( ["2026.9.11.4--ci.0", "2026.9.11.3-alpha.0.ci.0", "2026.9.11"] );

        // A non CI publication supersedes them all: only the releases (and the other branch) remain.
        f.Add( TestModel.SampleProfile( "2026.9.11.5" ) );
        f.RemoveSupersededCIProfiles( TestModel.TV( "2026.9.11.5" ) )
         .Select( v => v.ToString() )
         .ShouldBe( ["2026.9.11.4--ci.0"] );
        f.Profiles.Select( p => p.Version.ToString() )
         .ShouldBe( ["2026.9.11.5", "2026.9.11.3-alpha.0.ci.0", "2026.9.11"] );

        // 3 files move: the two deletions and the new release. "2026.9.11.4--ci.0" has been added and
        // superseded without ever being saved, so it never reaches the disk at all.
        f.Save().ShouldBe( 3 );
        f.Reload();
        f.LoadErrors.ShouldBeEmpty();
        f.Profiles.Select( p => p.Version.ToString() )
         .ShouldBe( ["2026.9.11.5", "2026.9.11.3-alpha.0.ci.0", "2026.9.11"] );
    }

    [Test]
    public void a_deprecated_CI_profile_is_never_removed()
    {
        var root = GetCleanFolder( nameof( a_deprecated_CI_profile_is_never_removed ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "2026.9.11--ci.0" ) );
        f.Deprecate( TestModel.TV( "2026.9.11--ci.0" ) ).ShouldBeTrue();

        // A deprecated profile is a record of a problem: superseding it doesn't erase it.
        f.Add( TestModel.SampleProfile( "2026.9.11.1" ) );
        f.RemoveSupersededCIProfiles( TestModel.TV( "2026.9.11.1" ) ).ShouldBeEmpty();
        f.Profiles.Select( p => p.Version.ToString() ).ShouldBe( ["2026.9.11.1", "2026.9.11--ci.0"] );
    }

    [Test]
    public void a_removed_profile_does_not_free_its_number()
    {
        var root = GetCleanFolder( nameof( a_removed_profile_does_not_free_its_number ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "2026.9.11" ) );
        f.Add( TestModel.SampleProfile( "2026.9.11.1" ) );
        f.Save().ShouldBe( 2 );

        f.Remove( TestModel.TV( "2026.9.11.1" ) ).ShouldBeTrue();
        f.CreateNewProfileVersion( CSVersionKind.Stable, utcNow: _day254 ).ToString().ShouldBe( "2026.9.11.2" );

        // The mark lives in the folder, not in a stored counter: once the removal is saved, the surviving
        // maximum is what carries it. This is why RemoveSupersededCIProfiles never removes the maximum.
        f.Save().ShouldBe( 1 );
        f.Reload();
        f.CreateNewProfileVersion( CSVersionKind.Stable, utcNow: _day254 ).ToString().ShouldBe( "2026.9.11.1" );
    }

    [Test]
    public void the_index_always_carries_the_stable_group_even_when_it_is_empty()
    {
        var root = GetCleanFolder( nameof( the_index_always_carries_the_stable_group_even_when_it_is_empty ) );
        var f = new PublishedFolder( root );

        f.IndexFilePath.ShouldBe( Path.Combine( root, PublishedFolder.IndexFileName ) );
        ReadIndex( f ).ShouldBe( """
            {
              "Alive": {
                "(stable)": []
              },
              "Deprecated": {
                "(stable)": []
              }
            }
            """ );

        // Nothing changed: Save writes no file at all, index included.
        f.Save().ShouldBe( 0 );
        File.Exists( f.IndexFilePath ).ShouldBeFalse();
    }

    [Test]
    public void the_index_groups_by_branch_orders_descending_and_splits_alive_from_deprecated()
    {
        var root = GetCleanFolder( nameof( the_index_groups_by_branch_orders_descending_and_splits_alive_from_deprecated ) );
        var f = new PublishedFolder( root );

        foreach( var v in new[] { "2026.11.20", "2026.11.20.1", "2026.11.21",
                                  "2026.11.21--ci.3", "2026.11.22--ci.0",
                                  "2026.11.21-alpha", "2026.11.21.1-alpha.0.ci.7",
                                  "2026.11.21-zulu.0.ci.12",
                                  "2026.11.21-0.some-explo" } )
        {
            f.Add( TestModel.SampleProfile( v ) );
        }
        // "2026.11.20.1" leaves the middle of the stable group. "2026.11.21-alpha" is the only regular alpha,
        // so that branch must disappear from "Alive" - while its CI line, "alpha-ci", stays there: a branch's CI
        // builds and its regular versions never share a list, and here they do not even share a set.
        f.Deprecate( TestModel.TV( "2026.11.20.1" ) ).ShouldBeTrue();
        f.Deprecate( TestModel.TV( "2026.11.21-alpha" ) ).ShouldBeTrue();

        f.Save().ShouldBe( 9, "The index is not one of them." );

        // "2026.11.21" and "2026.11.21--ci.3" are the same branch at the same numbers and still land in two lists.
        File.ReadAllText( f.IndexFilePath ).ShouldBe( """
            {
              "Alive": {
                "(stable)": [
                  "2026.11.21",
                  "2026.11.20"
                ],
                "(stable-ci)": [
                  "2026.11.22--ci.0",
                  "2026.11.21--ci.3"
                ],
                "alpha-ci": [
                  "2026.11.21.1-alpha.0.ci.7"
                ],
                "explo/some-explo": [
                  "2026.11.21-0.some-explo"
                ],
                "zulu-ci": [
                  "2026.11.21-zulu.0.ci.12"
                ]
              },
              "Deprecated": {
                "(stable)": [
                  "2026.11.20.1"
                ],
                "alpha": [
                  "2026.11.21-alpha"
                ]
              }
            }
            """ );
    }

    [Test]
    public void the_index_is_refreshed_only_when_a_profile_file_changed()
    {
        var root = GetCleanFolder( nameof( the_index_is_refreshed_only_when_a_profile_file_changed ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        f.Save().ShouldBe( 1 );
        File.ReadAllText( f.IndexFilePath ).ShouldContain( "2026.11.20" );

        // A Save that changes nothing doesn't touch the index: deleting it is how we can tell.
        File.Delete( f.IndexFilePath );
        f.Save().ShouldBe( 0 );
        File.Exists( f.IndexFilePath ).ShouldBeFalse();

        // Removing the last profile brings it back, empty.
        f.Remove( TestModel.TV( "2026.11.20" ) ).ShouldBeTrue();
        f.Save().ShouldBe( 1 );
        File.ReadAllText( f.IndexFilePath ).ShouldBe( """
            {
              "Alive": {
                "(stable)": []
              },
              "Deprecated": {
                "(stable)": []
              }
            }
            """ );
    }

    [Test]
    public void the_index_is_never_read_back_as_a_profile()
    {
        var root = GetCleanFolder( nameof( the_index_is_never_read_back_as_a_profile ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "2026.11.20" ) );
        f.Save().ShouldBe( 1 );
        File.Exists( f.IndexFilePath ).ShouldBeTrue();

        // "index" is not a version: the file this folder writes is invisible to its own discovery.
        f.Reload();
        f.Profiles.Select( p => p.Version.ToString() ).ShouldBe( ["2026.11.20"] );
        f.LoadErrors.ShouldBeEmpty();
    }

    // The index file is utf-8 without a BOM and uses "\r\n", like every profile file.
    static string ReadIndex( PublishedFolder f ) => Encoding.UTF8.GetString( f.CreateIndexUtf8Bytes() );

    [Test]
    public void a_new_profile_version_skips_an_unreadable_file_that_Save_would_replace()
    {
        var root = GetCleanFolder( nameof( a_new_profile_version_skips_an_unreadable_file_that_Save_would_replace ) );
        File.WriteAllText( Path.Combine( root, "v2026.9.11.json" ), "not a profile" );

        var f = new PublishedFolder( root );

        f.GetLoadError( TestModel.TV( "2026.9.11" ) ).ShouldNotBeNull();
        f.CreateNewProfileVersion( CSVersionKind.Stable, utcNow: _day254 ).ToString().ShouldBe( "2026.9.11.1" );
    }
}
