using CK.Core;
using CK.Packaging.Abstractions;
using CKli;
using CKli.Core;
using CKli.Publish.Plugin;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The upstream profiles: "deps update" records the profile of each World Reference it aligned on
/// (&lt;Reference&gt;&lt;Profile /&gt;&lt;/Reference&gt;), the regular builds check before any build that the World still
/// consumes what it produces, and a publication names it in its <see cref="PublishedProfile.Upstreams"/>.
/// </summary>
public class UpstreamProfilesTests
{
    /// <summary>
    /// The referenced Stack "Ref" publishes "R.Lib" 1.0.2; the "Test" Stack's "X-App" consumes "R.Lib" 1.0.1 and
    /// references "Ref".
    /// </summary>
    static async Task<(FakeBuildStack Ref, FakeBuildStack Stack, FakeBuildRepo App, PublishedProfile RefProfile)> ArrangeAsync( FakeBuildTestEnv testEnv )
    {
        var refStack = await testEnv.CreateStackAsync( "Ref", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rLib = await refStack.DefaultWorld.CreateRepoAsync( "R-Lib", "v1.0.1" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rLib.WorkingFolderPath, branchName: null );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, refStack.DefaultWorld.WorldRoot, "publish", "--regular" )).ShouldBeTrue();
        var refProfile = PublishedProfiles( refStack ).First();
        refProfile.ProducedPackages["R.Lib"].Version.ToString().ShouldBe( "1.0.2" );

        var stack = await testEnv.CreateStackAsync( "Test", Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var rApp = await stack.DefaultWorld.CreateRepoAsync( "X-App", "v0.1.0" ).ConfigureAwait( false );
        using( var e = rApp.CreateEditor() )
        {
            e.AddOrUpdateReference( rApp.DefaultProjectName, "R.Lib", SVersion.Parse( "1.0.1" ) );
        }
        (await CKliCommands.ExecAsync( TestHelper.Monitor, stack.DefaultWorld.WorldRoot, "world", "reference", "set", refStack.Remotes.StackUri.ToString() )).ShouldBeTrue();
        return (refStack, stack, rApp, refProfile);
    }

    [Test]
    public async Task deps_update_records_the_profile_and_the_publication_names_it_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (refStack, stack, rApp, refProfile) = await ArrangeAsync( testEnv ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        // A --dry-run records nothing.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "deps", "update", "--all", "--dry-run" )).ShouldBeTrue();
        RecordedProfiles( stack ).ShouldBeEmpty();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "deps", "update", "--all" )).ShouldBeTrue();
        RecordedProfiles( stack ).ShouldBe( [$"""<Profile Version="{refProfile.Version}" />"""] );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish", "--regular" )).ShouldBeTrue();
        var published = PublishedProfiles( stack ).First();
        published.DirectDependencies.ShouldContain( p => p.ToString() == "R.Lib@1.0.2" );
        var upstream = published.Upstreams.ShouldHaveSingleItem();
        upstream.StackUrl.ShouldBe( refProfile.StackUrl );
        upstream.World.ShouldBe( refProfile.World );
        upstream.Version.ShouldBe( refProfile.Version );
    }

    [Test]
    public async Task a_mixed_upstream_refuses_a_regular_build_before_any_build_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (refStack, stack, rApp, refProfile) = await ArrangeAsync( testEnv ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "deps", "update", "--all" )).ShouldBeTrue();

        // The reference is moved back by hand on "dev/stable": the World no more consumes what the recorded profile produces.
        using( var e = rApp.CreateEditor() )
        {
            e.AddOrUpdateReference( rApp.DefaultProjectName, "R.Lib", SVersion.Parse( "1.0.1" ), branchName: "dev/stable" );
        }
        var refs = GetRefs( rApp );
        foreach( var command in new[] { new[] { "build", "--regular" }, ["publish", "--regular", "--dry-run"], ["publish", "--regular"] } )
        {
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, command )).ShouldBeFalse();
                logs.ShouldContain( l => l.Contains( "doesn't consume what its recorded profile" )
                                         && l.Contains( "a mixed upstream" )
                                         && l.Contains( "'X-App' consumes 'R.Lib@1.0.1'" )
                                         && l.Contains( "produces '1.0.2'" ) );
            }
        }
        // Nothing has been built: "dev/stable" has not been integrated, no version tag.
        GetRefs( rApp ).ShouldBe( refs );

        // A CI build doesn't check.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "build" )).ShouldBeTrue();
    }

    [Test]
    public async Task a_CI_publication_with_a_mixed_upstream_warns_and_names_no_upstream_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (refStack, stack, rApp, refProfile) = await ArrangeAsync( testEnv ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "deps", "update", "--all" )).ShouldBeTrue();
        using( var e = rApp.CreateEditor() )
        {
            e.AddOrUpdateReference( rApp.DefaultProjectName, "R.Lib", SVersion.Parse( "1.0.1" ), branchName: "dev/stable" );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "publish" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "a mixed upstream" ) && l.Contains( "will not name this reference as an upstream" ) );
        }
        PublishedProfiles( stack ).First().Upstreams.ShouldBeEmpty();
    }

    [Test]
    public async Task closing_a_branch_removes_its_recorded_profiles_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var (refStack, stack, rApp, refProfile) = await ArrangeAsync( testEnv ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "deps", "update", "--all" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, rApp.Root, "branch", "open", "alpha" )).ShouldBeTrue();
        // "Ref" has no "alpha" branch: the alpha record is its root profile (the parent fallback).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "deps", "update", "--all", "--branch", "alpha" )).ShouldBeTrue();
        RecordedProfiles( stack ).ShouldBe( [$"""<Profile Version="{refProfile.Version}" />""",
                                             $"""<Profile Branch="alpha" Version="{refProfile.Version}" />"""] );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "branch", "close", "alpha", "--discard" )).ShouldBeTrue();
        RecordedProfiles( stack ).ShouldBe( [$"""<Profile Version="{refProfile.Version}" />"""] );
    }

    static List<PublishedProfile> PublishedProfiles( FakeBuildStack stack )
    {
        var folder = stack.StackRoot.AppendPart( StackRepository.PublicStackName ).AppendPart( "Published" );
        return new PublishedFolder( folder ).Profiles.ToList();
    }

    // The "Test" Stack's World definition file.
    static List<string> RecordedProfiles( FakeBuildStack stack )
    {
        var file = stack.StackRoot.AppendPart( StackRepository.PublicStackName ).AppendPart( "Test.xml" );
        return XDocument.Load( file ).Root!.Descendants( CKli.Core.XNames.Reference )
                                            .Elements( CKli.Core.XNames.Profile )
                                            .Select( e => e.ToString() )
                                            .ToList();
    }

    static Dictionary<string, string> GetRefs( FakeBuildRepo r )
    {
        using var e = r.CreateEditor();
        return e.GitRepository.Repository.Refs.ToDictionary( x => x.CanonicalName, x => x.TargetIdentifier );
    }
}
