using CK.Core;
using CKli.ArtifactHandler.Plugin;
using NUnit.Framework;
using Shouldly;
using System.Collections.Immutable;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// <see cref="BuildContentInfo"/> is the annotation of every version tag, so its textual form is a
/// stored format: these tests are about it staying readable in both directions.
/// </summary>
public class BuildContentInfoTests
{
    static ImmutableArray<PackageInstance> Packages( params string[] instances )
        => [.. instances.Select( s =>
        {
            var i = s.IndexOf( '@' );
            return new PackageInstance( s[..i], SVersion.Parse( s[(i + 1)..] ) );
        } )];

    /// <summary>
    /// The three original sections round trip, and the transitive packages are simply absent from the
    /// text when they have not been recorded.
    /// </summary>
    [Test]
    public void an_unrecorded_transitive_set_writes_no_section()
    {
        var c = new BuildContentInfo( Packages( "A@1.0.0", "B@2.0.0" ),
                                      produced: ["P.One", "P.Two"],
                                      assetFileNames: ["asset.zip"] );
        c.HasTransitive.ShouldBeFalse();
        c.ToString().ShouldBe( """
            2 Consumed Packages: A@1.0.0, B@2.0.0
            2 Produced Packages: P.One, P.Two
            1 Asset Files: asset.zip

            """.ReplaceLineEndings() );

        BuildContentInfo.TryParse( c.ToString(), out var back ).ShouldBeTrue();
        back!.HasTransitive.ShouldBeFalse();
        back.Transitive.IsDefault.ShouldBeTrue();
        back.ShouldBe( c );
    }

    /// <summary>
    /// A recorded set is a fourth section.
    /// </summary>
    [Test]
    public void a_recorded_transitive_set_is_a_fourth_section()
    {
        var c = new BuildContentInfo( Packages( "A@1.0.0" ),
                                      produced: ["P.One"],
                                      assetFileNames: ["asset.zip"],
                                      transitive: Packages( "C@3.0.0", "D@4.0.1--ci.2" ) );
        c.HasTransitive.ShouldBeTrue();
        c.ToString().ShouldBe( """
            1 Consumed Packages: A@1.0.0
            1 Produced Packages: P.One
            1 Asset Files: asset.zip
            2 Transitive Packages: C@3.0.0, D@4.0.1--ci.2

            """.ReplaceLineEndings() );

        BuildContentInfo.TryParse( c.ToString(), out var back ).ShouldBeTrue();
        back!.HasTransitive.ShouldBeTrue();
        back.Transitive.Select( p => p.ToString() ).ShouldBe( ["C@3.0.0", "D@4.0.1--ci.2"] );
        back.ShouldBe( c );
    }

    /// <summary>
    /// An empty section is written as its label with nothing after it, so the transitive section can follow
    /// a "0 Asset Files:" one immediately. Every real annotation of a repository that generates no asset has
    /// that shape, which makes this the parse that matters most.
    /// </summary>
    [Test]
    public void a_zero_asset_section_does_not_swallow_the_transitive_one()
    {
        var c = new BuildContentInfo( Packages( "A@1.0.0" ),
                                      produced: ["P.One"],
                                      assetFileNames: [],
                                      transitive: Packages( "C@3.0.0", "D@4.0.0" ) );

        BuildContentInfo.TryParse( c.ToString(), out var back ).ShouldBeTrue();
        back!.AssetFileNames.ShouldBeEmpty();
        back.HasTransitive.ShouldBeTrue();
        back.Transitive.Select( p => p.ToString() ).ShouldBe( ["C@3.0.0", "D@4.0.0"] );
        back.ShouldBe( c );
    }

    /// <summary>
    /// A verbatim annotation written by a CKli that predates the transitive packages, taken from a real
    /// version tag ("local/v0.15.0--ci.27" of this very repository, shortened). It parses, and what it does
    /// not say is not invented.
    /// <para>
    /// The trailing space of the empty "0 Asset Files: " section is deliberate and is what the writer
    /// produces: it is spelled out here rather than hidden at the end of a raw string literal line.
    /// </para>
    /// </summary>
    [Test]
    public void a_real_annotation_written_before_the_transitive_packages_parses()
    {
        var nl = System.Environment.NewLine;
        var annotation = "3 Consumed Packages: CK.Monitoring@25.1.0, CK.SVersion@0.2.4, LibGit2Sharp@0.31.0" + nl
                         + "2 Produced Packages: CKli, CKli.Core" + nl
                         + "0 Asset Files: " + nl;

        BuildContentInfo.TryParse( annotation, out var info ).ShouldBeTrue();
        info!.Consumed.Select( p => p.ToString() )
             .ShouldBe( ["CK.Monitoring@25.1.0", "CK.SVersion@0.2.4", "LibGit2Sharp@0.31.0"] );
        info.Produced.ShouldBe( ["CKli", "CKli.Core"] );
        info.AssetFileNames.ShouldBeEmpty();
        info.HasTransitive.ShouldBeFalse();
        info.Transitive.IsDefault.ShouldBeTrue();

        // Re-writing it gives the original text back: nothing is added to an annotation that has no
        // transitive packages recorded.
        info.ToString().ShouldBe( annotation );
    }

    /// <summary>
    /// "Recorded and empty" is NOT "not recorded": a restore that brings nothing is a statement, an
    /// annotation written before the transitive packages existed is not. The distinction survives the
    /// round trip and <see cref="BuildContentInfo.Equals(BuildContentInfo?)"/> respects it.
    /// </summary>
    [Test]
    public void a_recorded_empty_transitive_set_differs_from_an_unrecorded_one()
    {
        var unrecorded = new BuildContentInfo( Packages( "A@1.0.0" ), ["P"], [] );
        var empty = new BuildContentInfo( Packages( "A@1.0.0" ), ["P"], [], transitive: [] );

        empty.HasTransitive.ShouldBeTrue();
        empty.ToString().ShouldEndWith( "0 Transitive Packages: " + System.Environment.NewLine );
        unrecorded.ShouldNotBe( empty );

        BuildContentInfo.TryParse( unrecorded.ToString(), out var backUnrecorded ).ShouldBeTrue();
        BuildContentInfo.TryParse( empty.ToString(), out var backEmpty ).ShouldBeTrue();
        backUnrecorded!.HasTransitive.ShouldBeFalse();
        backEmpty!.HasTransitive.ShouldBeTrue();
        backUnrecorded.ShouldNotBe( backEmpty );
    }

    /// <summary>
    /// The transparency guarantee, in both directions.
    /// <para>
    /// Forward (a CKli that predates the transitive packages reading a new annotation): the reader parses
    /// the sections it knows and never requires the text to be fully consumed, so what matters is that the
    /// new text is the old text plus an appended section - asserted here as a prefix, which is the property
    /// the old reader relies on.
    /// </para>
    /// <para>
    /// Backward (this CKli reading an old annotation): the three section text parses, with the transitive
    /// set unrecorded.
    /// </para>
    /// </summary>
    [Test]
    public void the_transitive_section_is_appended_so_both_directions_stay_readable()
    {
        var consumed = Packages( "A@1.0.0", "B@2.0.0" );
        var threeSections = new BuildContentInfo( consumed, ["P.One"], ["asset.zip"] );
        var fourSections = new BuildContentInfo( consumed, ["P.One"], ["asset.zip"],
                                                 transitive: Packages( "C@3.0.0" ) );

        // Forward: the four section text starts with exactly the three section one.
        fourSections.ToString().ShouldStartWith( threeSections.ToString() );

        // Backward: an old annotation (three sections) is read, and what it does not say is not invented.
        BuildContentInfo.TryParse( threeSections.ToString(), out var old ).ShouldBeTrue();
        old!.Consumed.Select( p => p.ToString() ).ShouldBe( ["A@1.0.0", "B@2.0.0"] );
        old.Produced.ShouldBe( ["P.One"] );
        old.AssetFileNames.ShouldBe( ["asset.zip"] );
        old.HasTransitive.ShouldBeFalse();

        // And a malformed fourth section is not fatal: it is ignored like anything a reader does not know.
        BuildContentInfo.TryParse( threeSections + "this is not a section" + System.Environment.NewLine,
                                   out var lenient ).ShouldBeTrue();
        lenient!.HasTransitive.ShouldBeFalse();
        lenient.ShouldBe( old );
    }

    /// <summary>
    /// 'dotnet package list --include-transitive --format json' is read into the two sets. The same
    /// package appears once per project and per target framework: the result is deduplicated and sorted.
    /// </summary>
    [Test]
    public void the_package_list_json_is_read_into_the_two_sets()
    {
        const string json = """
            {
              "version": 1,
              "parameters": "--include-transitive",
              "projects": [
                {
                  "path": "One/One.csproj",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "topLevelPackages": [
                        { "id": "Zed.Top", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0" },
                        { "id": "Abc.Top", "requestedVersion": "[2.0.0,)", "resolvedVersion": "2.1.0" }
                      ],
                      "transitivePackages": [
                        { "id": "Deep.One", "resolvedVersion": "9.0.0" }
                      ]
                    }
                  ]
                },
                {
                  "path": "Two/Two.csproj",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "topLevelPackages": [
                        { "id": "Abc.Top", "requestedVersion": "2.0.0", "resolvedVersion": "2.1.0" }
                      ],
                      "transitivePackages": [
                        { "id": "Deep.One", "resolvedVersion": "9.0.0" },
                        { "id": "Deep.Two", "resolvedVersion": "8.0.1" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        BuildResult.ReadConsumedPackages( TestHelper.Monitor, json, "test", out var top, out var transitive )
                   .ShouldBeTrue();
        // The requestedVersion is deliberately not captured: the resolvedVersion is what was built against.
        top.Select( p => p.ToString() ).ShouldBe( ["Abc.Top@2.1.0", "Zed.Top@1.0.0"] );
        transitive.Select( p => p.ToString() ).ShouldBe( ["Deep.One@9.0.0", "Deep.Two@8.0.1"] );

        // Both are strictly sorted, which is what BuildContentInfo requires.
        Should.NotThrow( () => new BuildContentInfo( top, [], [], transitive ) );
    }

    /// <summary>
    /// Without '--include-transitive' the property is absent from the json, not empty: reading such an
    /// output yields no transitive package rather than failing.
    /// </summary>
    [Test]
    public void a_package_list_json_without_the_transitive_flag_reads_as_none()
    {
        const string json = """
            {
              "version": 1,
              "parameters": "",
              "projects": [
                {
                  "path": "One/One.csproj",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "topLevelPackages": [
                        { "id": "Abc.Top", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        BuildResult.ReadConsumedPackages( TestHelper.Monitor, json, "test", out var top, out var transitive )
                   .ShouldBeTrue();
        top.Select( p => p.ToString() ).ShouldBe( ["Abc.Top@1.0.0"] );
        transitive.ShouldBeEmpty();
    }
}
