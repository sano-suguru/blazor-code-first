namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// A native `switch` as the last statement of a design-time expression getter (ARCHITECTURE.md
/// §2.3/§5.3): the block is region-wrapped, and each section's content is drawn through a synthesized
/// `RenderFragment` rather than statically assigned -- the same treatment as a native `if`/`else`
/// (<see cref="BodyTransplantIfTests"/>).
/// </summary>
public sealed class BodyTransplantSwitchTests
{
    /// <summary>A component with a field <c>_mode</c> and a <c>Body</c> getter of <c>$GETTER$</c>.</summary>
    private const string Host = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private int _mode;

            protected override View Body
            $GETTER$
        }
        """;

    private static GeneratorRunResult Run(string getter) =>
        CompilationTestHost.RunGenerator(Host.Replace("$GETTER$", getter));

    private const string SwitchGetter = """
        {
                get
                {
                    switch (_mode)
                    {
                        case 1:
                            return Span["one"];
                        default:
                            return Span["other"];
                    }
                }
            }
        """;

    [Fact]
    public void Body_WhenGetterEndsInNativeSwitch_TransplantsIntoARegion()
    {
        var result = Run(SwitchGetter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
        Assert.Single(result.Diagnostics, d => d.Id == "BCF2002");

        Assert.Contains("__builder.OpenRegion(0);", generated);
        Assert.Contains("switch (_mode)", generated);
        Assert.Contains("case 1:", generated);
        Assert.Contains("default:", generated);
        Assert.Contains("__builder.CloseRegion();", generated);

        // Each section's content is a freshly synthesized RenderFragment lambda, not FragmentOf.
        Assert.DoesNotContain("FragmentOf", generated);
        Assert.Contains("__builder) =>", generated);

        // Both sections' outer AddContent share the region's one reserved sequence number.
        Assert.Equal(
            2,
            System.Text.RegularExpressions.Regex.Count(generated, "__builder\\.AddContent\\(1,"));

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenSwitchHasThreeSections_SharesOneRegionAndOneSequenceAcrossAllSections()
    {
        const string Getter = """
            {
                    get
                    {
                        switch (_mode)
                        {
                            case 1:
                                return Span["one"];
                            case 2:
                                return Span["two"];
                            default:
                                return Span["other"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");

        // Reported once for the whole switch, not once per section.
        Assert.Single(result.Diagnostics, d => d.Id == "BCF2002");

        // Exactly one region for the whole switch.
        Assert.Single(
            System.Text.RegularExpressions.Regex.Matches(generated, "__builder\\.OpenRegion\\("));

        // All three sections share the same sequence number: only one ever runs per render.
        Assert.Equal(
            3,
            System.Text.RegularExpressions.Regex.Count(generated, "__builder\\.AddContent\\(1,"));

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenSwitchSectionEndsInNestedIf_KeepsBranchingAndReportsBcf2002Once()
    {
        const string Getter = """
            {
                    get
                    {
                        switch (_mode)
                        {
                            case 1:
                                if (_mode > 0)
                                {
                                    return Span["positive"];
                                }
                                else
                                {
                                    return Span["zero-ish"];
                                }
                            default:
                                return Span["other"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");

        // Reported once for the whole switch, even though one of its sections itself degrades again.
        Assert.Single(result.Diagnostics, d => d.Id == "BCF2002");
        Assert.Contains("if (_mode > 0)", generated);

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenSwitchSectionEndsInNestedSwitch_KeepsBranchingAndReportsBcf2002Once()
    {
        const string Getter = """
            {
                    get
                    {
                        switch (_mode)
                        {
                            case 1:
                                switch (_mode)
                                {
                                    case 1:
                                        return Span["one-one"];
                                    default:
                                        return Span["one-other"];
                                }
                            default:
                                return Span["other"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");

        // Reported once for the whole outer switch, even though one of its sections itself degrades again.
        Assert.Single(result.Diagnostics, d => d.Id == "BCF2002");

        // Exactly one region for the whole construct: the nested switch shares the outer one's boundary,
        // the same way a nested `if` inside an `else` shares EmitTransplantedIf's one region.
        Assert.Single(
            System.Text.RegularExpressions.Regex.Matches(generated, "__builder\\.OpenRegion\\("));

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenSwitchHasNoDefaultAndNoTrailingStatement_ReportsBcf1004()
    {
        // The same reasoning as BodyTransplantIfTests.Body_WhenIfHasNoElseAndNoTrailingStatement_ReportsBcf1004:
        // a switch missing `default`, as the block's only/last statement, does not cover every path -- CS0161
        // on the original author source, before BCF analysis runs at all. What DOES reach
        // TryReadTransplantableSwitch and gets rejected is a `switch` that is NOT the block's last statement
        // (an explicit `return` after it): TryReadLeadingStatements only accepts a local declaration or an
        // expression statement as leading, and a SwitchStatementSyntax fails that test.
        const string Getter = """
            {
                    get
                    {
                        switch (_mode)
                        {
                            case 1:
                                return Span["one"];
                        }
                        return Span["fallback"];
                    }
                }
            """;
        var result = Run(Getter);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1004");
    }

    [Fact]
    public void Body_WhenSectionDeclaresReservedName_ReportsBcf1004()
    {
        const string Getter = """
            {
                    get
                    {
                        switch (_mode)
                        {
                            case 1:
                                var __builder = 1;
                                return Span[__builder.ToString()];
                            default:
                                return Span["other"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1004");
    }

    [Fact]
    public void Body_WhenMultipleCaseLabelsShareOneSection_EmitsBothLabelsForOneSharedBlock()
    {
        const string Getter = """
            {
                    get
                    {
                        switch (_mode)
                        {
                            case 1:
                            case 2:
                                return Span["one-or-two"];
                            default:
                                return Span["other"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
        Assert.Contains("case 1:", generated);
        Assert.Contains("case 2:", generated);

        // Two sections total (the shared "1 or 2" section, and "default"), not three: the shared labels
        // fall through into one content block, not one each.
        Assert.Equal(
            2,
            System.Text.RegularExpressions.Regex.Count(generated, "__builder\\.AddContent\\(1,"));

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenSectionUsesGotoCase_ReportsBcf1003()
    {
        // `goto case`/`goto default` is not one of AnalyzeSwitchSection's accepted trailing shapes (a
        // single `return`, or a nested `if`), so it falls through with no dedicated check -- the same
        // "unrecognized shape" path any other statement AnalyzeSwitchSection does not read takes. Unlike
        // a rejection TryReadTransplantableSwitch itself catches (BCF1004, the outer shape), this failure
        // is inside a section AnalyzeSwitch already accepted the switch's own shape for, so it reports the
        // same BCF1003 an unrecognized `If()`/ForEach-content shape would.
        const string Getter = """
            {
                    get
                    {
                        switch (_mode)
                        {
                            case 1:
                                goto case 2;
                            case 2:
                                return Span["two"];
                            default:
                                return Span["other"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }
}
