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
        /// Parses a Conformant SVersion.
        /// </summary>
        public static SVersion V( string version ) => SVersion.Parse( version, mustBeCSVersion: true );

        /// <summary>
        /// Creates a profile of 2 repositories and 3 packages, all in the provided version.
        /// </summary>
        public static PublishedProfile SampleProfile( string version = "1.2.3" )
        {
            return new PublishedProfile( StackUrl,
                                         World,
                                         V( version ),
                                         [Repo( "Two", 2, $"CK.Two@{version}" ),
                                          Repo( "One", 1, $"CK.One.Sub@{version}", $"CK.One@{version}" )] );
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
        f.Find( TestModel.V( "1.2.3" ) ).ShouldBeNull();
        f.GetLoadError( TestModel.V( "1.2.3" ) ).ShouldBeNull();
    }

    [TestCase( "1.2.3", @"v1.2.3.json" )]
    [TestCase( "1.2.3--ci.5", @"v1.2.3--ci.5.json" )]
    [TestCase( "1.3.0-alpha", @"alpha\v1.3.0-alpha.json" )]
    [TestCase( "1.3.0-zulu.4.ci.12", @"zulu\v1.3.0-zulu.4.ci.12.json" )]
    [TestCase( "0.0.0-0.some-explo", @"explo\some-explo\v0.0.0-0.some-explo.json" )]
    public void the_file_path_of_a_profile_follows_its_branch_name( string version, string relativePath )
    {
        var root = GetCleanFolder( nameof( the_file_path_of_a_profile_follows_its_branch_name ) );
        var f = new PublishedFolder( root );

        var expected = Path.Combine( root, relativePath.Replace( '\\', Path.DirectorySeparatorChar ) );
        f.GetProfileFilePath( TestModel.V( version ) ).ShouldBe( expected );
    }

    [Test]
    public void GetProfileFilePath_requires_a_conformant_version()
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( GetProfileFilePath_requires_a_conformant_version ) ) );
        var notCS = SVersion.Parse( "1.0.0-not-conformant" );

        Should.Throw<ArgumentException>( () => f.GetProfileFilePath( notCS ) )
              .Message.ShouldStartWith( "Version '1.0.0-not-conformant' must be a Conformant SVersion." );
    }

    [Test]
    public void added_profiles_are_written_by_Save_and_read_back()
    {
        var root = GetCleanFolder( nameof( added_profiles_are_written_by_Save_and_read_back ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "1.3.0-alpha" ) );
        f.Add( TestModel.SampleProfile( "0.0.0-0.some-explo" ) );
        f.IsDirty.ShouldBeTrue();
        // Nothing is written before Save.
        Directory.EnumerateFiles( root, "*.json", SearchOption.AllDirectories ).ShouldBeEmpty();

        f.Save().ShouldBe( 3 );
        f.IsDirty.ShouldBeFalse();
        f.Save().ShouldBe( 0, "Nothing more to do." );

        File.Exists( Path.Combine( root, "v1.2.3.json" ) ).ShouldBeTrue();
        File.Exists( Path.Combine( root, "alpha", "v1.3.0-alpha.json" ) ).ShouldBeTrue();
        File.Exists( Path.Combine( root, "explo", "some-explo", "v0.0.0-0.some-explo.json" ) ).ShouldBeTrue();

        // A brand new folder reads the files.
        var f2 = new PublishedFolder( root );
        f2.Profiles.Select( p => p.Version.ToString() )
                   .ShouldBe( new[] { "1.3.0-alpha", "1.2.3", "0.0.0-0.some-explo" },
                              "Ordered by descending version." );
        f2.LoadErrors.ShouldBeEmpty();

        var found = f2.Find( TestModel.V( "1.2.3" ) );
        found.ShouldNotBeNull();
        found.ToJsonString().ShouldBe( TestModel.SampleProfile( "1.2.3" ).ToJsonString() );
        // The file is the indented Json.
        File.ReadAllText( Path.Combine( root, "v1.2.3.json" ) ).ShouldBe( found.ToJsonString() );
    }

    [Test]
    public void Add_throws_when_the_version_already_exists()
    {
        var root = GetCleanFolder( nameof( Add_throws_when_the_version_already_exists ) );
        var f = new PublishedFolder( root );

        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        Should.Throw<InvalidOperationException>( () => f.Add( TestModel.SampleProfile( "1.2.3" ) ) )
              .Message.ShouldBe( "Profile '1.2.3' already exists." );

        f.Save().ShouldBe( 1 );

        // The saved profile is found by a new folder: Add still throws.
        var f2 = new PublishedFolder( root );
        Should.Throw<InvalidOperationException>( () => f2.Add( TestModel.SampleProfile( "1.2.3" ) ) );
    }

    [Test]
    public void Remove_deletes_the_file()
    {
        var root = GetCleanFolder( nameof( Remove_deletes_the_file ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "1.3.0-alpha" ) );
        f.Save().ShouldBe( 2 );

        f.Remove( TestModel.V( "1.2.4" ) ).ShouldBeFalse( "Unknown version." );
        f.Remove( TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f.Remove( TestModel.V( "1.2.3" ) ).ShouldBeFalse( "Already removed." );
        f.Find( TestModel.V( "1.2.3" ) ).ShouldBeNull();
        File.Exists( Path.Combine( root, "v1.2.3.json" ) ).ShouldBeTrue( "Not saved yet." );

        f.Save().ShouldBe( 1 );
        File.Exists( Path.Combine( root, "v1.2.3.json" ) ).ShouldBeFalse();

        new PublishedFolder( root ).Profiles.Select( p => p.Version.ToString() )
                                            .ShouldBe( new[] { "1.3.0-alpha" } );

        // A removed profile can be added again.
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Save().ShouldBe( 1 );
        File.Exists( Path.Combine( root, "v1.2.3.json" ) ).ShouldBeTrue();
    }

    [Test]
    public void Reload_forgets_the_pending_modifications()
    {
        var root = GetCleanFolder( nameof( Reload_forgets_the_pending_modifications ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Save().ShouldBe( 1 );

        f.Remove( TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f.Add( TestModel.SampleProfile( "2.0.0" ) );
        f.IsDirty.ShouldBeTrue();

        f.Reload();
        f.IsDirty.ShouldBeFalse();
        f.Find( TestModel.V( "1.2.3" ) ).ShouldNotBeNull();
        f.Find( TestModel.V( "2.0.0" ) ).ShouldBeNull();
    }

    [Test]
    public void Deprecate_updates_the_file()
    {
        var root = GetCleanFolder( nameof( Deprecate_updates_the_file ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Save().ShouldBe( 1 );

        f.Deprecate( TestModel.V( "9.9.9" ) ).ShouldBeFalse( "Unknown version." );
        f.Deprecate( TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f.Deprecate( TestModel.V( "1.2.3" ) ).ShouldBeFalse( "Idempotent." );
        f.Find( TestModel.V( "1.2.3" ) )!.IsDeprecated.ShouldBeTrue();

        f.Save().ShouldBe( 1 );
        new PublishedFolder( root ).Find( TestModel.V( "1.2.3" ) )!.IsDeprecated.ShouldBeTrue();
    }

    [Test]
    public void OnDeprecatedPackage_deprecates_every_profile_that_offers_the_package()
    {
        var root = GetCleanFolder( nameof( OnDeprecatedPackage_deprecates_every_profile_that_offers_the_package ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "1.2.4" ) );
        f.Add( TestModel.SampleProfile( "1.3.0-alpha" ) );
        f.Save().ShouldBe( 3 );

        // A brand new folder: OnDeprecatedPackage reads every file.
        var f2 = new PublishedFolder( root );
        f2.OnDeprecatedPackage( "CK.Unknown", TestModel.V( "1.2.3" ) ).ShouldBeFalse();
        f2.OnDeprecatedPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f2.OnDeprecatedPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeFalse( "Already deprecated." );

        f2.Profiles.Where( p => p.IsDeprecated )
                   .Select( p => p.Version.ToString() )
                   .ShouldBe( new[] { "1.2.3" }, "Only the profile that offers CK.One@1.2.3." );

        f2.Save().ShouldBe( 1 );
        new PublishedFolder( root ).Find( TestModel.V( "1.2.3" ) )!.IsDeprecated.ShouldBeTrue();
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
    public void OnFixedPackages_supersedes_the_profiles_that_offer_a_fixed_package()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_supersedes_the_profiles_that_offer_a_fixed_package ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "1.2.4" ) );
        f.Add( TestModel.SampleProfile( "1.3.0-alpha" ) );
        f.Save().ShouldBe( 3 );

        // A brand new folder: OnFixedPackages reads every file.
        var f2 = new PublishedFolder( root );
        var created = f2.OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) );

        // Only the profile that offers CK.One@1.2.3 is superseded. Its successor keeps its Major.Minor and
        // takes the next free Patch: "1.2.4" is taken, so "1.2.5".
        created.Length.ShouldBe( 1 );
        var p = created[0];
        p.Version.ToString().ShouldBe( "1.2.5" );
        p.IsDeprecated.ShouldBeFalse();
        p.StackUrl.ShouldBe( TestModel.StackUrl );
        p.World.FullName.ShouldBe( TestModel.World.FullName );

        // Only the fixed package moves: the offer is otherwise the one it supersedes.
        p.Packages["CK.One"].Version.ToString().ShouldBe( "1.2.9" );
        p.Packages["CK.One.Sub"].Version.ToString().ShouldBe( "1.2.3" );
        p.Packages["CK.Two"].Version.ToString().ShouldBe( "1.2.3" );

        // The superseded profile records what was published and is left untouched.
        f2.Find( TestModel.V( "1.2.3" ) )!.Packages["CK.One"].Version.ToString().ShouldBe( "1.2.3" );

        f2.Save().ShouldBe( 1 );
        new PublishedFolder( root ).Profiles
                                   .Select( x => x.Version.ToString() )
                                   .ShouldBe( new[] { "1.3.0-alpha", "1.2.5", "1.2.4", "1.2.3" } );
    }

    [Test]
    public void a_superseding_profile_stays_on_its_branch()
    {
        var root = GetCleanFolder( nameof( a_superseding_profile_stays_on_its_branch ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.3.0-alpha" ) );
        f.Save().ShouldBe( 1 );

        var created = new PublishedFolder( root ).OnFixedPackages( Fixed( "1.3.0-alpha", "1.3.1" ) );
        created.Length.ShouldBe( 1 );
        created[0].Version.ToString().ShouldBe( "1.3.1-alpha" );
    }

    [Test]
    public void OnFixedPackages_leaves_a_deprecated_profile_locked()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_leaves_a_deprecated_profile_locked ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Deprecate( TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f.Save().ShouldBe( 1 );

        new PublishedFolder( root ).OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) )
                                   .ShouldBeEmpty( "A deprecated profile is locked." );
    }

    [Test]
    public void OnFixedPackages_is_idempotent()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_is_idempotent ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Save().ShouldBe( 1 );

        var f2 = new PublishedFolder( root );
        f2.OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) ).Length.ShouldBe( 1 );
        f2.OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) )
          .ShouldBeEmpty( "The superseding profile already carries that offer." );
        f2.Save().ShouldBe( 1 );

        // And across a reload: retrying an interrupted fix publication adds nothing.
        new PublishedFolder( root ).OnFixedPackages( Fixed( "1.2.3", "1.2.9" ) ).ShouldBeEmpty();
    }

    [Test]
    public void OnFixedPackages_ignores_a_package_no_profile_offers()
    {
        var root = GetCleanFolder( nameof( OnFixedPackages_ignores_a_package_no_profile_offers ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Save().ShouldBe( 1 );

        var f2 = new PublishedFolder( root );
        f2.OnFixedPackages( Fixed( "9.9.9", "9.9.10" ) ).ShouldBeEmpty( "No profile offers CK.One@9.9.9." );
        f2.OnFixedPackages( new Dictionary<PackageInstance, SVersion>() ).ShouldBeEmpty();
        f2.IsDirty.ShouldBeFalse();
    }

    [Test]
    public void OnExpiredPackage_removes_every_profile_that_offers_the_package()
    {
        var root = GetCleanFolder( nameof( OnExpiredPackage_removes_every_profile_that_offers_the_package ) );
        var f = new PublishedFolder( root );
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "1.2.4" ) );
        f.Add( TestModel.SampleProfile( "1.3.0-alpha" ) );
        f.Save().ShouldBe( 3 );

        // A brand new folder: OnExpiredPackage reads every file.
        var f2 = new PublishedFolder( root );
        f2.OnExpiredPackage( "CK.Unknown", TestModel.V( "1.2.3" ) ).ShouldBeFalse();
        f2.OnExpiredPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeTrue();
        f2.OnExpiredPackage( "CK.One", TestModel.V( "1.2.3" ) ).ShouldBeFalse( "Already removed." );

        f2.Profiles.Select( p => p.Version.ToString() )
                   .ShouldBe( new[] { "1.3.0-alpha", "1.2.4" }, "The profile that offers CK.One@1.2.3 is gone." );

        f2.Save().ShouldBe( 1 );
        File.Exists( Path.Combine( root, "v1.2.3.json" ) ).ShouldBeFalse();
        // An expired profile is deleted, not deprecated: nothing is left to read.
        var f3 = new PublishedFolder( root );
        f3.Find( TestModel.V( "1.2.3" ) ).ShouldBeNull();
        f3.GetLoadError( TestModel.V( "1.2.3" ) ).ShouldBeNull();
    }

    [Test]
    public void OnExpiredPackage_ignores_a_profile_that_offers_another_version()
    {
        var root = GetCleanFolder( nameof( OnExpiredPackage_ignores_a_profile_that_offers_another_version ) );
        var f = new PublishedFolder( root );
        // SampleProfile puts its packages in the profile's own version: only "1.2.3" offers CK.One@1.2.3.
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Add( TestModel.SampleProfile( "1.2.4" ) );
        f.Save().ShouldBe( 2 );

        new PublishedFolder( root ).OnExpiredPackage( "CK.One", TestModel.V( "9.9.9" ) ).ShouldBeFalse();
        new PublishedFolder( root ).Profiles.Count().ShouldBe( 2 );
    }

    [Test]
    public void an_unreadable_file_is_a_load_error_and_can_be_replaced()
    {
        var root = GetCleanFolder( nameof( an_unreadable_file_is_a_load_error_and_can_be_replaced ) );
        File.WriteAllText( Path.Combine( root, "v1.2.3.json" ), "{ this is not json" );
        File.WriteAllText( Path.Combine( root, "v1.2.4.json" ),
                           TestModel.SampleProfile( "1.2.3" ).ToJsonString() );

        var f = new PublishedFolder( root );

        f.Find( TestModel.V( "1.2.3" ) ).ShouldBeNull();
        // A syntax error is a JsonReaderException (a specialized JsonException).
        f.GetLoadError( TestModel.V( "1.2.3" ) ).ShouldBeAssignableTo<JsonException>();
        // The file name and the profile's version must agree.
        f.GetLoadError( TestModel.V( "1.2.4" ) )
         .ShouldBeAssignableTo<JsonException>()!
         .Message.ShouldEndWith( "contains the version '1.2.3'." );

        f.LoadErrors.Select( e => e.Version.ToString() ).ShouldBe( new[] { "1.2.4", "1.2.3" } );
        f.Profiles.ShouldBeEmpty();

        // An invalid file doesn't prevent the add: Save replaces it.
        f.Add( TestModel.SampleProfile( "1.2.3" ) );
        f.Save().ShouldBe( 1 );

        var f2 = new PublishedFolder( root );
        f2.Find( TestModel.V( "1.2.3" ) ).ShouldNotBeNull();
        f2.GetLoadError( TestModel.V( "1.2.3" ) ).ShouldBeNull();
    }

    [Test]
    public void a_missing_file_is_not_a_load_error()
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( a_missing_file_is_not_a_load_error ) ) );

        f.Find( TestModel.V( "1.2.3" ) ).ShouldBeNull();
        f.GetLoadError( TestModel.V( "1.2.3" ) ).ShouldBeNull();
    }

    [Test]
    public void files_that_are_not_at_their_canonical_path_are_ignored()
    {
        var root = GetCleanFolder( nameof( files_that_are_not_at_their_canonical_path_are_ignored ) );
        var good = TestModel.SampleProfile( "1.2.3" ).ToJsonString();

        // Not a version.
        File.WriteAllText( Path.Combine( root, "index.json" ), good );
        // A version with a prefix.
        File.WriteAllText( Path.Combine( root, "profile-v1.2.3.json" ), good );
        // A stable version in a branch folder.
        Directory.CreateDirectory( Path.Combine( root, "alpha" ) );
        File.WriteAllText( Path.Combine( root, "alpha", "v1.2.3.json" ), good );
        // A prerelease at the root.
        File.WriteAllText( Path.Combine( root, "v1.3.0-alpha.json" ),
                           TestModel.SampleProfile( "1.3.0-alpha" ).ToJsonString() );
        // The only canonical file.
        File.WriteAllText( Path.Combine( root, "v2.0.0.json" ),
                           TestModel.SampleProfile( "2.0.0" ).ToJsonString() );

        var f = new PublishedFolder( root );

        f.Profiles.Select( p => p.Version.ToString() ).ShouldBe( new[] { "2.0.0" } );
        f.LoadErrors.ShouldBeEmpty();
    }

    // 2026 is not a leap year: the 254th day is the 11th of September.
    static readonly DateTime _day254 = new DateTime( 2026, 9, 11, 13, 45, 0, DateTimeKind.Utc );

    [TestCase( CSVersionKind.Stable, false, "2026.254.0" )]
    [TestCase( CSVersionKind.Stable, true, "2026.254.0--ci.0" )]
    [TestCase( CSVersionKind.Alpha, false, "2026.254.0-alpha" )]
    [TestCase( CSVersionKind.Alpha, true, "2026.254.0-alpha.0.ci.0" )]
    [TestCase( CSVersionKind.Zulu, false, "2026.254.0-zulu" )]
    [TestCase( CSVersionKind.Zulu, true, "2026.254.0-zulu.0.ci.0" )]
    public void a_new_profile_version_is_time_based( CSVersionKind branchKind, bool isCIBuild, string expected )
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( a_new_profile_version_is_time_based ) ) );

        var v = f.CreateNewProfileVersion( branchKind, isCIBuild: isCIBuild, utcNow: _day254 );
        v.ToString().ShouldBe( expected );
        // The version is always a Conformant SVersion: the PublishedProfile constructor requires it.
        v.VersionKind.ShouldBe( branchKind );
        v.IsCI.ShouldBe( isCIBuild );
    }

    [Test]
    public void a_new_profile_version_of_an_exploratory_branch_carries_its_name()
    {
        var f = new PublishedFolder( GetCleanFolder( nameof( a_new_profile_version_of_an_exploratory_branch_carries_its_name ) ) );

        f.CreateNewProfileVersion( CSVersionKind.Exploratory, "some-explo", utcNow: _day254 )
         .ToString().ShouldBe( "2026.254.0-0.some-explo" );
        f.CreateNewProfileVersion( CSVersionKind.Exploratory, "some-explo", isCIBuild: true, utcNow: _day254 )
         .ToString().ShouldBe( "2026.254.0-0.some-explo.0.ci.0" );
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
    public void the_patch_of_a_new_profile_version_is_incremented_until_it_is_free()
    {
        var root = GetCleanFolder( nameof( the_patch_of_a_new_profile_version_is_incremented_until_it_is_free ) );
        var f = new PublishedFolder( root );

        f.CreateNewProfileVersion( CSVersionKind.Alpha, utcNow: _day254 ).ToString().ShouldBe( "2026.254.0-alpha" );
        // A pending Add is enough: the folder doesn't need to be saved.
        f.Add( TestModel.SampleProfile( "2026.254.0-alpha" ) );
        f.CreateNewProfileVersion( CSVersionKind.Alpha, utcNow: _day254 ).ToString().ShouldBe( "2026.254.1-alpha" );

        f.Add( TestModel.SampleProfile( "2026.254.1-alpha" ) );
        f.Save().ShouldBe( 2 );
        f.Reload();
        f.CreateNewProfileVersion( CSVersionKind.Alpha, utcNow: _day254 ).ToString().ShouldBe( "2026.254.2-alpha" );

        // Another branch (and the CI builds of a branch) has its own patch sequence.
        f.CreateNewProfileVersion( CSVersionKind.Stable, utcNow: _day254 ).ToString().ShouldBe( "2026.254.0" );
        f.CreateNewProfileVersion( CSVersionKind.Alpha, isCIBuild: true, utcNow: _day254 )
         .ToString().ShouldBe( "2026.254.0-alpha.0.ci.0" );
    }

    [Test]
    public void a_new_profile_version_skips_an_unreadable_file_that_Save_would_replace()
    {
        var root = GetCleanFolder( nameof( a_new_profile_version_skips_an_unreadable_file_that_Save_would_replace ) );
        File.WriteAllText( Path.Combine( root, "v2026.254.0.json" ), "not a profile" );

        var f = new PublishedFolder( root );

        f.GetLoadError( TestModel.V( "2026.254.0" ) ).ShouldNotBeNull();
        f.CreateNewProfileVersion( CSVersionKind.Stable, utcNow: _day254 ).ToString().ShouldBe( "2026.254.1" );
    }
}
