using CK.Core;
using CKli.ShallowSolution.Plugin;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;

namespace Plugins.Tests;

/// <summary>
/// <see cref="PackageBounds"/> resolution: the World's &lt;VersionTag&gt;&lt;Packages&gt; bounds are an ordered
/// list of rules whose name may hold '*' wildcards - each one matching any sequence of characters - and <b>the
/// first one that matches wins</b>. These are pure lookups (no fixture, no git) and they are the rules the
/// "ckli deps update" fixtures rely on.
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
    public void a_trailing_star_covers_every_identifier_that_starts_with_the_prefix()
    {
        var bounds = Create( ("Microsoft.AspNetCore.*", "8.0.0[LockMajor]") );

        bounds.TryGet( "Microsoft.AspNetCore.Http", out var b, out var origin ).ShouldBeTrue();
        b.ToString().ShouldBe( "8.0.0[LockMajor]" );
        origin.ShouldBe( "Microsoft.AspNetCore.*", "The report names the rule, not the identifier." );

        bounds.TryGet( "Microsoft.AspNetCore.Authentication.OpenIdConnect", out _, out _ ).ShouldBeTrue();
        // The literal part is matched as it is written, it is not a namespace: the '.' must be there too.
        bounds.TryGet( "Microsoft.AspNetCore", out _, out _ ).ShouldBeFalse();
        bounds.TryGet( "Microsoft.Extensions.Configuration", out _, out _ ).ShouldBeFalse();
    }

    [Test]
    public void a_star_stands_for_any_sequence_of_characters_wherever_it_is()
    {
        // A leading star is a suffix family...
        var suffix = Create( ("*.Abstractions", "1.0.0") );
        suffix.TryGet( "CK.Core.Abstractions", out _, out _ ).ShouldBeTrue();
        suffix.TryGet( "Microsoft.Extensions.Configuration.Abstractions", out _, out _ ).ShouldBeTrue();
        suffix.TryGet( "CK.Core.Abstractions.Impl", out _, out _ ).ShouldBeFalse();

        // ...an inner one groups by role across whatever sits in the middle.
        var infix = Create( ("X.*.Thing", "2.0.0") );
        infix.TryGet( "X.IO.Thing", out _, out var origin ).ShouldBeTrue();
        origin.ShouldBe( "X.*.Thing" );
        infix.TryGet( "X.DB.Thing", out _, out _ ).ShouldBeTrue();
        infix.TryGet( "X.A.B.Thing", out _, out _ ).ShouldBeTrue( "A '*' crosses the dots: it is not one segment." );
        infix.TryGet( "X..Thing", out _, out _ ).ShouldBeTrue( "A '*' matches an empty sequence too." );
        infix.TryGet( "X.Thing", out _, out _ ).ShouldBeFalse( "Both literals must be there, one after the other." );
        infix.TryGet( "Y.IO.Thing", out _, out _ ).ShouldBeFalse();
        infix.TryGet( "X.IO.Thing.Impl", out _, out _ ).ShouldBeFalse( "The last literal is anchored at the end." );

        // Several stars are matched in order.
        var many = Create( ("*.DB.*.Engine*", "3.0.0") );
        many.TryGet( "CK.DB.User.Engine", out _, out _ ).ShouldBeTrue();
        many.TryGet( "CK.DB.Zone.Engine.Tests", out _, out _ ).ShouldBeTrue();
        many.TryGet( "CK.DB.Engine", out _, out _ ).ShouldBeFalse( "The '.Engine' literal must follow a '.DB.' one." );

        // A name without any star is one exact identifier: it matches nothing else.
        var exact = Create( ("CK.Core", "1.0.0") );
        exact.TryGet( "CK.Core", out _, out _ ).ShouldBeTrue();
        exact.TryGet( "CK.Core.Abstractions", out _, out _ ).ShouldBeFalse();
        exact.TryGet( "My.CK.Core", out _, out _ ).ShouldBeFalse();
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

        // Covering is decided on what the other rule may expand to, not on its text: a literal of one rule
        // can never match across a '*' of the other, since a name's literals never hold one.
        var r = Create( ("X.*", "1.0.0"),
                        ("X.*.Thing", "1.0.0"),
                        ("*AB*", "1.0.0"),
                        ("*A*B*", "1.0.0") ).Rules;
        r[0].Covers( r[1] ).ShouldBeTrue( "Everything 'X.*.Thing' matches starts with 'X.'." );
        r[1].Covers( r[0] ).ShouldBeFalse( "'X.*' matches 'X.zzz', which 'X.*.Thing' does not." );
        r[3].Covers( r[2] ).ShouldBeTrue( "'AB' is an 'A' then a 'B': everything '*AB*' matches, '*A*B*' matches." );
        r[2].Covers( r[3] ).ShouldBeFalse( "'*A*B*' matches 'AxB', which '*AB*' does not." );
    }

    [Test]
    public void a_name_must_hold_one_literal_character_and_no_two_consecutive_stars()
    {
        // GetPackagesConfiguration refuses these with a message that names the <Package> element: this is the
        // last line of defense, not the error a user sees.
        Should.Throw<ArgumentException>( () => Create( ("Microsoft.**", "1.0.0") ) );
        Should.Throw<ArgumentException>( () => Create( ("*", "1.0.0") ) );
        Should.Throw<ArgumentException>( () => Create( ("**", "1.0.0") ) );
        Should.Throw<ArgumentException>( () => Create( ("  ", "1.0.0") ) );
        // Everything else is a name: a star may sit anywhere, and a name without one is exact.
        Create( ("Mid*dle.Package", "1.0.0") ).Rules[0].IsPattern.ShouldBeTrue();
        Create( ("*.Abstractions", "1.0.0") ).Rules[0].IsPattern.ShouldBeTrue();
        Create( ("NoStar", "1.0.0") ).Rules[0].IsPattern.ShouldBeFalse();
    }
}
