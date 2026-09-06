using CK.Core;
using CKli.BranchModel.Plugin;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class BranchNamespaceTests
{
    [Test]
    public void default_BranchNamespace()
    {
        var defaultBranchNamespace = new BranchNamespace( null, null, [] );
        defaultBranchNamespace.Branches.Select( b => b.Name ).ShouldBe( ["stable"] );
        defaultBranchNamespace.Root.ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        defaultBranchNamespace.Root.Parent.ShouldBeNull();
        defaultBranchNamespace.Root.LinkType.ShouldBe( BranchLinkType.None );
        defaultBranchNamespace.Root.DevName.ShouldBe( "dev/stable" );
        defaultBranchNamespace.ByName.ShouldHaveSingleItem();
        defaultBranchNamespace.ByName["stable"].ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        defaultBranchNamespace.GetMainLine().ShouldBe( "stable" );

        defaultBranchNamespace = new BranchNamespace( "Net8", null, [] );
        defaultBranchNamespace.Branches.Select( b => b.Name ).ShouldBe( ["Net8/stable"] );
        defaultBranchNamespace.Root.ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        defaultBranchNamespace.Root.Parent.ShouldBeNull();
        defaultBranchNamespace.Root.LinkType.ShouldBe( BranchLinkType.None );
        defaultBranchNamespace.Root.DevName.ShouldBe( "Net8/dev/stable" );
        defaultBranchNamespace.ByName.ShouldHaveSingleItem();
        defaultBranchNamespace.ByName["Net8/stable"].ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        defaultBranchNamespace.GetMainLine().ShouldBe( "Net8/stable" );
    }

    [Test]
    public void mainline_updates()
    {
        var def = new BranchNamespace( null, null, [] );

        var (ns, b) = def.AddOrUpdate( BranchLinkType.Full, CSVersionKind.Romeo );
        b.LinkType.ShouldBe( BranchLinkType.Full );
        b.Name.ShouldBe( "romeo" );
        b.Parent.ShouldBeSameAs( ns.Root );
        ns.GetMainLine().ShouldBe( "stable => romeo" );

        // No change.
        (ns, b) = ns.AddOrUpdate( BranchLinkType.Full, CSVersionKind.Romeo );
        b.LinkType.ShouldBe( BranchLinkType.Full );
        b.Name.ShouldBe( "romeo" );
        b.Parent.ShouldBeSameAs( ns.Root );
        ns.GetMainLine().ShouldBe( "stable => romeo" );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.CI, CSVersionKind.Zulu );
        b.LinkType.ShouldBe( BranchLinkType.CI );
        b.Name.ShouldBe( "zulu" );
        b.Parent.ShouldBeSameAs( ns.Root );
        ns.GetMainLine().ShouldBe( "stable -> zulu => romeo" );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.CI, CSVersionKind.Zulu );
        b.LinkType.ShouldBe( BranchLinkType.CI );
        b.Name.ShouldBe( "zulu" );
        b.Parent.ShouldBeSameAs( ns.Root );
        ns.GetMainLine().ShouldBe( "stable -> zulu => romeo" );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.Manual, CSVersionKind.Alpha );
        b.LinkType.ShouldBe( BranchLinkType.Manual );
        b.Name.ShouldBe( "alpha" );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "romeo" );
        ns.GetMainLine().ShouldBe( "stable -> zulu => romeo |✋ alpha" );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.Release, CSVersionKind.Delta );
        b.LinkType.ShouldBe( BranchLinkType.Release );
        b.Name.ShouldBe( "delta" );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "romeo" );
        ns.GetMainLine().ShouldBe( "stable -> zulu => romeo |> delta |✋ alpha" );

        ns = ns.Remove( ns.FindRequired( "romeo" ) );
        ns.GetMainLine().ShouldBe( "stable -> zulu |> delta |✋ alpha" );

        ns = ns.Remove( ns.FindRequired( "zulu" ) );
        ns.GetMainLine().ShouldBe( "stable |> delta |✋ alpha" );

        ns = ns.Remove( ns.FindRequired( "alpha" ) );
        ns.GetMainLine().ShouldBe( "stable |> delta" );

        ns = ns.Remove( ns.FindRequired( "delta" ) );
        ns.GetMainLine().ShouldBe( "stable" );
    }

    [Test]
    public void explo_updates()
    {
        var def = new BranchNamespace( null, "stable -> zulu => romeo |> delta |✋ alpha", [] );
        def.GetExplo().Select( e => e.ToString() ).Concatenate( "" ).ShouldBe( "" );

        var (ns, b) = def.AddOrUpdateExplo( "explo/v-next" );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "alpha" );
        ns.ToString().ShouldBe( """
            stable -> zulu => romeo |> delta |✋ alpha
            <Explo Name="explo/v-next" Parent="alpha" />
            """ );

        ns = ns.Remove( b );
        ns.ToString().ShouldBe( "stable -> zulu => romeo |> delta |✋ alpha" );

        (ns, b) = ns.AddOrUpdateExplo( "explo/v-next", BranchLinkType.Release, ns.FindRequired( "romeo" ) );
        b.LinkType.ShouldBe( BranchLinkType.Release );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "romeo" );
        ns.ToString().ShouldBe( """
            stable -> zulu => romeo |> delta |✋ alpha
            <Explo Name="explo/v-next" Link="Release" Parent="romeo" />
            """ );

        (ns, b) = ns.AddOrUpdateExplo( "explo/again", BranchLinkType.Manual, b );
        b.LinkType.ShouldBe( BranchLinkType.Manual );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "explo/v-next" );
        ns.ToString().ShouldBe( """
            stable -> zulu => romeo |> delta |✋ alpha
            <Explo Name="explo/v-next" Link="Release" Parent="romeo">
              <Explo Name="explo/again" Link="Manual" />
            </Explo>
            """ );

        ns = ns.Remove( ns.FindRequired( "romeo" ) );
        ns.ToString().ShouldBe( """
            stable -> zulu |> delta |✋ alpha
            <Explo Name="explo/v-next" Link="Release" Parent="zulu">
              <Explo Name="explo/again" Link="Manual" />
            </Explo>
            """ );

        ns = ns.Remove( ns.FindRequired( "explo/v-next" ) );
        ns.ToString().ShouldBe( """
            stable -> zulu |> delta |✋ alpha
            <Explo Name="explo/again" Link="Manual" Parent="zulu" />
            """ );
    }

    /// <summary>
    /// A "-ci" suffix qualifies a branch name to tell its CI builds from its regular versions - the Publish
    /// plugin's "Published/index.json" does exactly that - so an exploratory name, the only branch name that
    /// is free, must not be able to spell one. The standard prerelease names ("alpha" to "zulu") are fixed
    /// and by design none of them collides, which is why only the exploratory ones are checked.
    /// </summary>
    [TestCase( "explo/spike-ci" )]
    [TestCase( "explo/ci-spike" )]
    [TestCase( "explo/x-ci" )]
    public void an_exploratory_branch_name_cannot_be_confused_with_a_ci_line( string branchName )
    {
        var def = new BranchNamespace( null, "stable -> zulu => romeo |> delta |✋ alpha", [] );

        Should.Throw<ArgumentException>( () => def.AddOrUpdateExplo( branchName ) )
              .Message.ShouldContain( """must not start with "ci-" nor end with "-ci".""" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            BranchName.TryParseBranchName( TestHelper.Monitor, branchName, out _ ).ShouldBeFalse();
            logs.ShouldContain( $"""Invalid '{branchName}'. An exploratory branch name must not start with "ci-" nor end with "-ci".""" );
        }
    }

    /// <summary>
    /// The rule is about the whole name: "ci" alone, or a name that merely contains "-ci-", is fine.
    /// </summary>
    [TestCase( "explo/ci" )]
    [TestCase( "explo/spike-ci-2" )]
    [TestCase( "explo/cix" )]
    public void an_exploratory_branch_name_that_only_looks_like_a_ci_line_is_valid( string branchName )
    {
        var def = new BranchNamespace( null, "stable -> zulu => romeo |> delta |✋ alpha", [] );

        def.AddOrUpdateExplo( branchName ).Item2.Name.ShouldBe( branchName );
        BranchName.TryParseBranchName( TestHelper.Monitor, branchName, out _ ).ShouldBeTrue();
    }
}
