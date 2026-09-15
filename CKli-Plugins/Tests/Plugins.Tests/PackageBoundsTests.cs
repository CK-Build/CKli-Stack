using CK.Core;
using CKli.ShallowSolution.Plugin;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;

namespace Plugins.Tests;

/// <summary>
/// <see cref="PackageBounds"/> resolution: the World's &lt;VersionTag&gt;&lt;Packages&gt; bounds are an ordered
/// list of rules - an exact package identifier or a "Prefix*" pattern - and <b>the first one that matches wins</b>.
/// These are pure lookups (no fixture, no git) and they are the rules the "ckli deps update" fixtures rely on.
/// </summary>
public class PackageBoundsTests
{
    static SVersionBound B( string bound )
    {
        SVersionBound.TryParse( bound, out var b ).ShouldBeTrue( bound );
        return b;
    }

    // The rules in declaration order, which is their priority order.
    static PackageBounds Create( params (string Name, string Bound)[] rules )
    {
        var r = new List<(string, SVersionBound)>();
        foreach( var (n, b) in rules ) r.Add( (n, B( b )) );
        return new PackageBounds( r );
    }

    [Test]
    public void a_pattern_covers_every_identifier_that_starts_with_its_prefix()
    {
        var bounds = Create( ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]") );

        bounds.TryGet( "Microsoft.AspNetCore.Http", out var b, out var origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "8.0.0[LockMajor]" );
        origin.ShouldBe( "Microsoft.AspNetCore.*", "The report names the rule, not the identifier." );

        bounds.TryGet( "Microsoft.AspNetCore.Authentication.OpenIdConnect", out _, out _ ).ShouldBeTrue();
        // The prefix is a prefix, not a namespace.
        bounds.TryGet( "Microsoft.AspNetCore", out _, out _ ).ShouldBeFalse();
        bounds.TryGet( "Microsoft.Extensions.Configuration", out _, out _ ).ShouldBeFalse();
    }

    [Test]
    public void the_first_matching_rule_wins_and_nothing_else_is_considered()
    {
        // Declared before its family, the exact name is the family's exception...
        var exceptionFirst = Create( ("Microsoft.AspNetCore.Http", "7.0.1[Lock]"),
                                     ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]") );
        exceptionFirst.TryGet( "Microsoft.AspNetCore.Http", out var b, out var origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "7.0.1[Lock]" );
        origin.ShouldBe( "Microsoft.AspNetCore.Http" );
        exceptionFirst.TryGet( "Microsoft.AspNetCore.Routing", out b, out origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "8.0.0[LockMajor]" );
        origin.ShouldBe( "Microsoft.AspNetCore.*" );

        // ...declared after it, the very same exact name never matches: the family already answered.
        // Being exact buys no priority - only the order does.
        var familyFirst = Create( ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]"),
                                  ("Microsoft.AspNetCore.Http", "7.0.1[Lock]") );
        familyFirst.TryGet( "Microsoft.AspNetCore.Http", out b, out origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "8.0.0[LockMajor]" );
        origin.ShouldBe( "Microsoft.AspNetCore.*" );
    }

    [Test]
    public void a_coarse_pattern_declared_first_shadows_a_finer_one()
    {
        // Neither prefix length nor any other notion of specificity is taken into account: fine before coarse
        // is the order that expresses "the family, with this sub-family apart".
        var fineFirst = Create( ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]"), ("Microsoft.*", "6.0.0[Lock]") );
        fineFirst.TryGet( "Microsoft.AspNetCore.Http", out var b, out var origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "8.0.0[LockMajor]" );
        origin.ShouldBe( "Microsoft.AspNetCore.*" );
        fineFirst.TryGet( "Microsoft.Extensions.Configuration", out b, out origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "6.0.0[Lock]" );
        origin.ShouldBe( "Microsoft.*" );

        var coarseFirst = Create( ("Microsoft.*", "6.0.0[Lock]"), ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]") );
        coarseFirst.TryGet( "Microsoft.AspNetCore.Http", out b, out origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "6.0.0[Lock]" );
        origin.ShouldBe( "Microsoft.*", "The coarse rule matched first: the finer one is unreachable." );
    }

    [Test]
    public void package_identifiers_and_patterns_are_case_insensitive()
    {
        var bounds = Create( ("CK.CanaryPackage", "1.0.0"), ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]") );

        bounds.TryGet( "ck.canarypackage", out _, out var origin ).ShouldBeTrue();
        origin.ShouldBe( "CK.CanaryPackage", "The origin is the configured name, not the one that was looked up." );
        bounds.TryGet( "MICROSOFT.ASPNETCORE.HTTP", out _, out origin ).ShouldBeTrue();
        origin.ShouldBe( "Microsoft.AspNetCore.*" );
    }

    [Test]
    public void an_empty_bounds_matches_nothing()
    {
        PackageBounds.Empty.IsEmpty.ShouldBeTrue();
        PackageBounds.Empty.TryGet( "Any.Package", out _, out var origin ).ShouldBeFalse();
        origin.ShouldBeNull();
        Create().IsEmpty.ShouldBeTrue();
        Create( ("A.*", "1.0.0") ).IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void a_rule_covers_the_rules_that_it_makes_unreachable()
    {
        // Covers is what detects an unreachable <Package>: since the first match wins, a rule declared after
        // one that covers it can never answer. VersionTagPlugin warns about exactly this.
        var rules = Create( ("Microsoft.*", "6.0.0[Lock]"),
                            ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]"),
                            ("Microsoft.AspNetCore.Http", "7.0.1[Lock]"),
                            ("CK.CanaryPackage", "1.0.0") ).Rules;
        var all = rules[0];
        var family = rules[1];
        var exact = rules[2];
        var other = rules[3];

        // A pattern covers the identifiers and the narrower patterns that start with its prefix.
        all.Covers( family ).ShouldBeTrue();
        all.Covers( exact ).ShouldBeTrue();
        family.Covers( exact ).ShouldBeTrue();
        all.Covers( other ).ShouldBeFalse();

        // ...and nothing wider than itself.
        family.Covers( all ).ShouldBeFalse();
        exact.Covers( family ).ShouldBeFalse();

        // An exact rule matches one identifier: it covers only the same name (which is the duplicate error).
        exact.Covers( exact ).ShouldBeTrue();
        exact.Covers( other ).ShouldBeFalse();
    }

    [Test]
    public void a_name_is_an_exact_identifier_or_a_non_empty_prefix_followed_by_a_single_star()
    {
        // GetPackagesConfiguration refuses these with a message that names the <Package> element: this is the
        // last line of defense, not the error a user sees.
        Should.Throw<ArgumentException>( () => Create( ("Mid*dle.Package", "1.0.0") ) );
        Should.Throw<ArgumentException>( () => Create( ("Microsoft.**", "1.0.0") ) );
        Should.Throw<ArgumentException>( () => Create( ("*", "1.0.0") ) );
        Should.Throw<ArgumentException>( () => Create( ("  ", "1.0.0") ) );
        // An exact name needs no star: this one is valid.
        Create( ("NoStar", "1.0.0") ).Rules[0].IsPattern.ShouldBeFalse();
    }
}
