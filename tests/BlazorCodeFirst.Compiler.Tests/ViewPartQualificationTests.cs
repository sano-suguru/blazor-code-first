using System.Globalization;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// How a <c>[ViewPart]</c> body's names are spelled once transplanted into a caller in another type,
/// where none of the author's using directives and none of their containing type's scope survives
/// (ARCHITECTURE.md §2.3). Each case here reaches its member from a syntactic position the transplant
/// cases beside it do not: a member-access receiver, and a name the author already qualified (#392).
/// </summary>
public sealed class ViewPartQualificationTests
{
    /// <summary>
    /// A part in a type of its own, so that expansion lands it in a scope sharing nothing with where it
    /// was written. <c>$MEMBER$</c> is the member the part reads and <c>$EXPRESSION$</c> how it reads it.
    /// </summary>
    private const string PartsFile = """
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public static class Parts
        {
            $MEMBER$

            [ViewPart]
            public static View Row() => Span[$EXPRESSION$];
        }
        """;

    private const string CallerFile = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Parts.Row();
        }
        """;

    private static GeneratorRunResult Run(string member, string expression) =>
        CompilationTestHost.RunGenerator(
            ("Parts.cs", PartsFile.Replace("$MEMBER$", member).Replace("$EXPRESSION$", expression)),
            ("C.cs", CallerFile));

    [Fact]
    public void ViewPart_WhenAStaticMemberIsTheReceiverOfAMemberAccess_QualifiesIt()
    {
        var result = Run("public static readonly List<string> Inner = new() { \"b\" };", "Inner.Count.ToString()");

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The receiver is the one name in 'Inner.Count.ToString()' that the caller's scope cannot resolve,
        // so it is the one that has to carry the qualification.
        Assert.Contains("global::Parts.Inner.Count.ToString()", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenAPrivateStaticMemberIsTheReceiverOfAMemberAccess_ReportsBCF1002()
    {
        var result = Run("private static readonly List<string> Inner = new() { \"b\" };", "Inner.Count.ToString()");

        // A name that is qualified is also checked for reachability: the same arm records both, so a
        // receiver skipped by the qualification was skipped by the accessibility check too.
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Inner", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ViewPart_WhenATypeIsWrittenAliasQualified_LeavesItAsWritten()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Data.cs", """
                public static class Data
                {
                    public static string Label => "x";
                }
                """),
            ("Parts.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public static class Parts
                {
                    [ViewPart]
                    public static View Row() => Span[global::Data.Label];
                }
                """),
            ("C.cs", CallerFile));

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // 'global::Data' is already everything the qualification would add, and a second one is not even
        // legal syntax.
        Assert.Contains("global::Data.Label", generated);
        Assert.DoesNotContain("global::global::", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}
