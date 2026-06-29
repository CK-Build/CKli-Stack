using CK.Core;
using CKli.BranchModel.Plugin;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Xml.Linq;

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

    }
}
