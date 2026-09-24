using CK.Core;
using CKli.BranchModel.Plugin;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class BranchNamespaceTests
{
    [Test]
    public void default_BranchNamespace()
    {
        var defaultBranchNamespace = new BranchNamespace( null, null );
        defaultBranchNamespace.Branches.Select( b => b.Name ).ShouldBe( ["stable"] );
        defaultBranchNamespace.Root.ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        defaultBranchNamespace.Root.Parent.ShouldBeNull();
        defaultBranchNamespace.Root.LinkType.ShouldBe( BranchLinkType.None );
        defaultBranchNamespace.Root.DevName.ShouldBe( "dev/stable" );
        defaultBranchNamespace.ByName.ShouldHaveSingleItem();
        defaultBranchNamespace.ByName["stable"].ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        defaultBranchNamespace.ToString().ShouldBe( """<BranchModel Root="stable" />""" );

        defaultBranchNamespace = new BranchNamespace( "Net8", null );
        defaultBranchNamespace.Branches.Select( b => b.Name ).ShouldBe( ["Net8/stable"] );
        defaultBranchNamespace.Root.ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        defaultBranchNamespace.Root.Parent.ShouldBeNull();
        defaultBranchNamespace.Root.LinkType.ShouldBe( BranchLinkType.None );
        defaultBranchNamespace.Root.DevName.ShouldBe( "Net8/dev/stable" );
        defaultBranchNamespace.ByName.ShouldHaveSingleItem();
        defaultBranchNamespace.ByName["Net8/stable"].ShouldBeSameAs( defaultBranchNamespace.Branches[0] );
        // The configuration is what the constructor above parses, so the LTS name is not part of it
        // (the constructor prepends it). BranchName.ConfigurationName is that form.
        defaultBranchNamespace.Root.ConfigurationName.ShouldBe( "stable" );
        defaultBranchNamespace.ToString().ShouldBe( """<BranchModel Root="stable" />""" );
        new BranchNamespace( "Net8", defaultBranchNamespace.ToConfiguration() )
            .ShouldBe( defaultBranchNamespace, "The configuration round trips." );
    }

    /// <summary>
    /// The configuration spells a link type by the very name the "--link" option of "ckli branch open" takes,
    /// and it always writes it - including the CI default: a World definition file states what is true instead
    /// of relying on a default its reader has to know. Reading stays tolerant: an absent Link is CI.
    /// </summary>
    [Test]
    public void the_configuration_always_writes_the_Link_and_round_trips()
    {
        var ns = Namespace( """
            <BranchModel Root="stable">
              <Prerelease Name="zulu" />
              <Prerelease Name="romeo" Link="Full" />
              <Explo Name="explo/v-next" Parent="romeo" />
            </BranchModel>
            """ );
        ns.FindRequired( "zulu" ).LinkType.ShouldBe( BranchLinkType.CI, "An absent Link is CI." );
        ns.FindRequired( "explo/v-next" ).LinkType.ShouldBe( BranchLinkType.CI );

        ns.ToString().ShouldBe( """
            <BranchModel Root="stable">
              <Prerelease Name="zulu" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
              <Explo Name="explo/v-next" Link="CI" Parent="romeo" />
            </BranchModel>
            """, "The CI default is written back explicitly." );

        new BranchNamespace( null, ns.ToConfiguration() ).ShouldBe( ns );
    }

    /// <summary>
    /// WriteConfiguration updates the element in place: the attributes and elements that are not the branch
    /// model's (AutoFixUselessBranch here) are left untouched - it is the live plugin configuration element.
    /// </summary>
    [Test]
    public void writing_the_configuration_keeps_the_other_attributes()
    {
        var e = XElement.Parse( """
            <BranchModel AutoFixUselessBranch="false">
              <Prerelease Name="zulu" Link="Release" />
            </BranchModel>
            """ );
        var ns = new BranchNamespace( null, e );
        (ns, _) = ns.AddOrUpdate( BranchLinkType.Full, CSVersionKind.Romeo );
        ns.WriteConfiguration( e );

        e.ToString().ShouldBe( """
            <BranchModel AutoFixUselessBranch="false" Root="stable">
              <Prerelease Name="zulu" Link="Release" />
              <Prerelease Name="romeo" Link="Full" />
            </BranchModel>
            """ );
    }

    /// <summary>
    /// The &lt;Prerelease&gt; element order is irrelevant: the CSemVer prerelease names carry a total order, so
    /// the parent chain is a function of the names alone. The parser sorts them and writes them back in
    /// decreasing stability order.
    /// </summary>
    [Test]
    public void the_prerelease_element_order_is_irrelevant()
    {
        var sorted = Namespace( """
            <BranchModel Root="stable">
              <Prerelease Name="zulu" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
              <Prerelease Name="alpha" Link="Manual" />
            </BranchModel>
            """ );
        var shuffled = Namespace( """
            <BranchModel Root="stable">
              <Prerelease Name="alpha" Link="Manual" />
              <Prerelease Name="zulu" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
            </BranchModel>
            """ );
        shuffled.ShouldBe( sorted );
        shuffled.GetDisplayTree().ShouldBe( """
            stable
              -> zulu
                => romeo
                  |✋ alpha
            """ );
        shuffled.ToString().ShouldBe( sorted.ToString(), "They are written back in decreasing stability order." );
    }

    /// <summary>
    /// A duplicate Name is the one thing the order cannot excuse: it is one branch with two link types.
    /// </summary>
    [Test]
    public void the_prerelease_names_are_checked()
    {
        Should.Throw<CKException>( () => Namespace( """
            <BranchModel Root="stable">
              <Prerelease Name="romeo" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
            </BranchModel>
            """ ) ).Message.ShouldContain( """Duplicate Prerelease Name="romeo""" );

        Should.Throw<CKException>( () => Namespace( """<BranchModel><Prerelease Name="explo" /></BranchModel>""" ) )
              .Message.ShouldContain( "Invalid Prerelease Name attribute" );

        Should.Throw<CKException>( () => Namespace( """<BranchModel Root="alpha" />""" ) )
              .Message.ShouldContain( "must not be one of the prerelease name nor 'explo'" );
    }

    [Test]
    public void mainline_updates()
    {
        var def = new BranchNamespace( null, null );

        var (ns, b) = def.AddOrUpdate( BranchLinkType.Full, CSVersionKind.Romeo );
        b.LinkType.ShouldBe( BranchLinkType.Full );
        b.Name.ShouldBe( "romeo" );
        b.Parent.ShouldBeSameAs( ns.Root );
        ns.GetDisplayTree().ShouldBe( """
            stable
              => romeo
            """ );

        // No change.
        (ns, b) = ns.AddOrUpdate( BranchLinkType.Full, CSVersionKind.Romeo );
        b.LinkType.ShouldBe( BranchLinkType.Full );
        b.Name.ShouldBe( "romeo" );
        b.Parent.ShouldBeSameAs( ns.Root );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.CI, CSVersionKind.Zulu );
        b.LinkType.ShouldBe( BranchLinkType.CI );
        b.Name.ShouldBe( "zulu" );
        b.Parent.ShouldBeSameAs( ns.Root );
        ns.GetDisplayTree().ShouldBe( """
            stable
              -> zulu
                => romeo
            """ );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.Manual, CSVersionKind.Alpha );
        b.LinkType.ShouldBe( BranchLinkType.Manual );
        b.Name.ShouldBe( "alpha" );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "romeo" );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.Release, CSVersionKind.Delta );
        b.LinkType.ShouldBe( BranchLinkType.Release );
        b.Name.ShouldBe( "delta" );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "romeo" );
        ns.GetDisplayTree().ShouldBe( """
            stable
              -> zulu
                => romeo
                  |> delta
                    |✋ alpha
            """ );
        ns.ToString().ShouldBe( """
            <BranchModel Root="stable">
              <Prerelease Name="zulu" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
              <Prerelease Name="delta" Link="Release" />
              <Prerelease Name="alpha" Link="Manual" />
            </BranchModel>
            """ );

        ns = ns.Remove( ns.FindRequired( "romeo" ) );
        ns.GetDisplayTree().ShouldBe( """
            stable
              -> zulu
                |> delta
                  |✋ alpha
            """ );

        ns = ns.Remove( ns.FindRequired( "zulu" ) );
        ns = ns.Remove( ns.FindRequired( "alpha" ) );
        ns.GetDisplayTree().ShouldBe( """
            stable
              |> delta
            """ );

        ns = ns.Remove( ns.FindRequired( "delta" ) );
        ns.GetDisplayTree().ShouldBe( "stable" );
    }

    /// <summary>
    /// BranchLinkType.None is "not specified" (this is what "ckli branch open" without --link submits): only the
    /// root branch can have it, since a &lt;Prerelease&gt; element always writes its Link. A new branch defaults
    /// to CI, an already opened one keeps its link type.
    /// </summary>
    [Test]
    public void a_mainline_branch_never_has_the_None_link_type()
    {
        var def = new BranchNamespace( null, null );

        var (ns, b) = def.AddOrUpdate( BranchLinkType.None, CSVersionKind.Romeo );
        b.LinkType.ShouldBe( BranchLinkType.CI, "A new branch defaults to CI." );

        (ns, b) = ns.AddOrUpdate( BranchLinkType.Full, CSVersionKind.Romeo );
        (ns, b) = ns.AddOrUpdate( BranchLinkType.None, CSVersionKind.Romeo );
        b.LinkType.ShouldBe( BranchLinkType.Full, "An opened branch keeps its link type." );
        ns.ToString().ShouldBe( """
            <BranchModel Root="stable">
              <Prerelease Name="romeo" Link="Full" />
            </BranchModel>
            """ );

        new BranchNamespace( null, ns.ToConfiguration() ).ShouldBe( ns );
    }

    /// <summary>
    /// In a LTS world every branch name carries the "{LTSName}/" prefix, but the configuration that
    /// WriteConfiguration()/ToConfiguration() write back never does: the constructor is what prepends it, and
    /// its Root parser rejects a name starting with the '@' of an LTSName. Writing the prefixed names made the
    /// world unloadable - which is what "ckli lts create" and any branch mutation in a LTS world used to produce.
    /// </summary>
    [Test]
    public void lts_namespace_configuration_round_trips()
    {
        var def = Namespace( """
            <BranchModel Root="stable">
              <Prerelease Name="zulu" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
            </BranchModel>
            """ );
        // 2 exploratory branches with 2 different parents: GetExplo() emits them as 2 top level elements,
        // each with its own Parent attribute.
        var (withExplo, _) = def.AddOrUpdateExplo( "explo/v-next", BranchLinkType.Release, def.FindRequired( "romeo" ) );
        (withExplo, _) = withExplo.AddOrUpdateExplo( "explo/spike", BranchLinkType.CI, withExplo.FindRequired( "zulu" ) );

        var lts = new BranchNamespace( "@net8", withExplo.ToConfiguration() );
        lts.Branches.Select( b => b.Name )
           .ShouldBe( ["@net8/stable", "@net8/zulu", "@net8/romeo", "@net8/explo/v-next", "@net8/explo/spike"] );
        lts.Branches.Select( b => b.ConfigurationName )
           .ShouldBe( ["stable", "zulu", "romeo", "explo/v-next", "explo/spike"] );
        lts.FindRequired( "@net8/explo/spike" ).Parent.ShouldNotBeNull().Name.ShouldBe( "@net8/zulu" );

        // GetDisplayTree() is the other direction: it keeps the ACTUAL branch names.
        lts.GetDisplayTree().ShouldBe( """
            @net8/stable
              -> @net8/zulu
                => @net8/romeo
                  |> @net8/explo/v-next
                -> @net8/explo/spike
            """ );

        lts.GetPrereleases().Select( e => e.ToString() ).Concatenate( "" )
           .ShouldBe( """<Prerelease Name="zulu" Link="CI" /><Prerelease Name="romeo" Link="Full" />""" );
        lts.GetExplo().Select( e => e.ToString() ).Concatenate( "" )
           .ShouldBe( """<Explo Name="explo/v-next" Link="Release" Parent="romeo" /><Explo Name="explo/spike" Link="CI" Parent="zulu" />""" );
        new BranchNamespace( "@net8", lts.ToConfiguration() ).ShouldBe( lts );

        // CreateForLTS keeps only the root branch: this is the configuration "ckli lts create" writes.
        var ltsRoot = def.CreateForLTS( "@net8" );
        ltsRoot.Branches.Select( b => b.Name ).ShouldBe( ["@net8/stable"] );
        ltsRoot.ToString().ShouldBe( """<BranchModel Root="stable" />""" );
        ltsRoot.GetPrereleases().ShouldBeEmpty();
        ltsRoot.GetExplo().ShouldBeEmpty();
        new BranchNamespace( "@net8", ltsRoot.ToConfiguration() ).ShouldBe( ltsRoot );
    }

    /// <summary>
    /// The "dev/" branch of a branch "X" is "dev/X" for the commands and "dev/X" (default World) or "@lts/dev/X" (LTS
    /// World, see BranchName.DevName) for git: all of these spellings designate the "dev/" branch of "X".
    /// </summary>
    [TestCase( null, "dev/stable", "stable", true )]
    [TestCase( null, "Dev/stable", "stable", true )]
    [TestCase( null, "stable", "stable", false )]
    [TestCase( null, "@net8/dev/stable", "@net8/dev/stable", false )]
    [TestCase( "@net8", "dev/@net8/stable", "@net8/stable", true )]
    [TestCase( "@net8", "@net8/dev/stable", "@net8/stable", true )]
    [TestCase( "@net8", "dev/stable", "stable", true )]
    [TestCase( "@net8", "@net8/stable", "@net8/stable", false )]
    [TestCase( "@net8", "@net8/developer", "@net8/developer", false )]
    [TestCase( "@net8", "@net9/dev/stable", "@net9/dev/stable", false )]
    public void RemoveDevPrefix_normalizes_the_dev_branch_names( string? ltsName, string name, string expected, bool expectedIsDev )
    {
        var ns = ltsName == null
                    ? Namespace( """<BranchModel Root="stable" />""" )
                    : new BranchNamespace( ltsName, Namespace( """<BranchModel Root="stable" />""" ).ToConfiguration() );
        ns.RemoveDevPrefix( name, out var isDev ).ShouldBe( expected );
        isDev.ShouldBe( expectedIsDev );
        if( expectedIsDev ) ns.FindRequired( expected ).DevName.ShouldBe( ltsName == null ? $"dev/{expected}" : $"{ltsName}/dev/stable" );
    }

    [Test]
    public void explo_updates()
    {
        var def = Namespace( """
            <BranchModel Root="stable">
              <Prerelease Name="zulu" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
              <Prerelease Name="delta" Link="Release" />
              <Prerelease Name="alpha" Link="Manual" />
            </BranchModel>
            """ );
        def.GetExplo().ShouldBeEmpty();

        var (ns, b) = def.AddOrUpdateExplo( "explo/v-next" );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "alpha" );
        ns.GetExplo().Select( e => e.ToString() ).Concatenate( "" )
          .ShouldBe( """<Explo Name="explo/v-next" Link="CI" Parent="alpha" />""" );

        ns = ns.Remove( b );
        ns.GetExplo().ShouldBeEmpty();

        (ns, b) = ns.AddOrUpdateExplo( "explo/v-next", BranchLinkType.Release, ns.FindRequired( "romeo" ) );
        b.LinkType.ShouldBe( BranchLinkType.Release );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "romeo" );

        (ns, b) = ns.AddOrUpdateExplo( "explo/again", BranchLinkType.Manual, b );
        b.LinkType.ShouldBe( BranchLinkType.Manual );
        b.Parent.ShouldNotBeNull().Name.ShouldBe( "explo/v-next" );
        ns.ToString().ShouldBe( """
            <BranchModel Root="stable">
              <Prerelease Name="zulu" Link="CI" />
              <Prerelease Name="romeo" Link="Full" />
              <Prerelease Name="delta" Link="Release" />
              <Prerelease Name="alpha" Link="Manual" />
              <Explo Name="explo/v-next" Link="Release" Parent="romeo">
                <Explo Name="explo/again" Link="Manual" />
              </Explo>
            </BranchModel>
            """ );

        ns = ns.Remove( ns.FindRequired( "romeo" ) );
        ns.GetExplo().Select( e => e.ToString() ).Concatenate( "" )
          .ShouldBe( """
            <Explo Name="explo/v-next" Link="Release" Parent="zulu">
              <Explo Name="explo/again" Link="Manual" />
            </Explo>
            """ );

        ns = ns.Remove( ns.FindRequired( "explo/v-next" ) );
        ns.GetExplo().Select( e => e.ToString() ).Concatenate( "" )
          .ShouldBe( """<Explo Name="explo/again" Link="Manual" Parent="zulu" />""" );
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
        var def = new BranchNamespace( null, null );

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
        var def = new BranchNamespace( null, null );

        def.AddOrUpdateExplo( branchName ).Item2.Name.ShouldBe( branchName );
        BranchName.TryParseBranchName( TestHelper.Monitor, branchName, out _ ).ShouldBeTrue();
    }

    static BranchNamespace Namespace( string configuration ) => new BranchNamespace( null, XElement.Parse( configuration ) );
}
