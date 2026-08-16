using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds the two landing-page figures that make a claim about this compiler to what the compiler
/// actually does (#247). Both were hand-maintained text a reader is invited to trust as output, and
/// every check the site has stayed green whichever way the compiler moved.
/// </summary>
/// <remarks>
/// <para>
/// <c>site/</c> is outside <c>BlazorCodeFirst.slnx</c>, so the site's own suite cannot reference the
/// compiler and this pair of assertions has to live on this side of that boundary. It is the same
/// arrangement as <see cref="PackageReadmeTests"/>, whose subject is likewise a published document
/// quoting emitted code: the figure is the fixture, the compiler is what is under test, and the file
/// is read from the repository by path.
/// </para>
/// <para>
/// Both figures elide something on purpose, because a figure is read at a glance. Each elision is
/// declared in the figure itself rather than assumed here — the generated one by a
/// <c>/* … */</c> placeholder standing for the expression the generator wraps, the diagnostic one by
/// the line breaks that keep the message off a phone's horizontal scrollbar. Nothing else in either
/// figure is allowed to differ.
/// </para>
/// </remarks>
public sealed partial class LandingPageFigureTests
{
    private static readonly string SiteDirectory =
        Path.Combine(GeneratedSourceSnapshot.FindRepositoryRoot(), "site");

    /// <summary>
    /// The landing page's comparison figure: <c>site/snippets/generated.cs</c> claims to be the
    /// <c>RenderTreeBuilder</c> calls the generator emits for <c>site/snippets/design-time.cs</c>.
    /// </summary>
    /// <remarks>
    /// The design-time snippet is an expression, not a component, so it is compiled inside the smallest
    /// component that can host it. <c>Save</c> exists here for the same reason: the figure names it, and
    /// a component the figure does not show is not part of what the pair claims.
    /// </remarks>
    [Fact]
    public void TheGeneratedFigure_IsWhatTheGeneratorEmitsForTheDesignTimeFigure()
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Figure : BodyComponentBase
            {
                private void Save()
                {
                }

                protected override View Body =>
                    {{ReadSnippet("design-time.cs")}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        CompilationTestHost.AssertOutputCompiles(result);

        var emitted = BuilderCalls.InTextOrder(Assert.Single(result.GeneratedSources).SourceText.ToString());
        var figure = BuilderCalls.InTextOrder(ReadSnippet("generated.cs"));

        // Equal counts alone would report two empty figures as agreement, and a guard that reports
        // nothing to compare as agreement is worse than no guard.
        Assert.True(
            !figure.IsEmpty,
            "site/snippets/generated.cs shows no frames, so there is nothing to hold the generator to.");

        Assert.True(
            figure.Length == emitted.Length,
            $"site/snippets/generated.cs shows {figure.Length} frames and the generator emits " +
            $"{emitted.Length}.\n\nfigure:\n{string.Join("\n", figure)}\n\nemitted:\n{string.Join("\n", emitted)}");

        foreach (var (shown, actual) in figure.Zip(emitted))
            AssertShows(shown, actual);
    }

    /// <summary>
    /// The landing page's build-failure figure quotes BCF3016's <c>messageFormat</c>, its ID and its
    /// severity, all three transcribed by hand into <c>CodeSamples.Diagnostic</c>.
    /// </summary>
    /// <remarks>
    /// The figure is not a snippet and cannot become one: a terminal message is not code, so no
    /// mechanism that reads a <c>.cs</c> file reaches it. What it renders is read out of its own source
    /// instead, by the rule the surface itself states — a literal in brackets is content the reader
    /// sees, and a literal in a decoration call is a class name. That leaves the figure free to change
    /// its markup without this test being taught the new shape.
    /// </remarks>
    [Fact]
    public void TheDiagnosticFigure_QuotesTheDescriptorTheCompilerDeclares()
    {
        var shown = RenderedText(Path.Combine(
            SiteDirectory, "BlazorCodeFirst.Site", "Content", "CodeSamples.cs"));

        var match = BuildError().Match(shown);
        Assert.True(
            match.Success,
            "The landing page's build-failure figure no longer reads as a build failure " +
            $"(<path>(<line>,<col>): <severity> <id>: <message>), so there is nothing to compare it " +
            $"with. It shows:\n{shown}");

        var descriptor = DiagnosticDescriptors.BCF3016;
        Assert.Equal(descriptor.Id, match.Groups["id"].Value);

        // Error and Warning are the two a build prints, and both are the enum's own name; the regex has
        // already held the figure to spelling it the way a build does.
        Assert.Equal(
            descriptor.DefaultSeverity.ToString(),
            match.Groups["severity"].Value,
            ignoreCase: true);

        // The figure breaks the message by hand so it does not need a horizontal scroll on a phone.
        // Each break replaces a space, which is the whole of what it is allowed to change.
        var quoted = match.Groups["message"].Value.Replace("\n", " ", StringComparison.Ordinal);
        var format = descriptor.MessageFormat.ToString();
        var around = format.Split("{0}");

        Assert.True(
            around.Length == 2,
            $"BCF3016 no longer takes exactly one message argument (its format is {format}), so the " +
            "figure's element name can no longer be read back out of what it shows.");

        Assert.True(
            Surrounds(around[0], around[1], quoted),
            "The landing page's build-failure figure no longer quotes BCF3016's messageFormat.\n\n" +
            $"figure:     {quoted}\ndescriptor: {format}");

        var tag = quoted[around[0].Length..^around[1].Length];
        Assert.True(
            KnownSymbols.IsVoidTag(tag),
            $"The figure reports BCF3016 against '{tag}', which is not a void element, so it shows a " +
            "diagnostic the compiler would not report.");
    }

    private static string ReadSnippet(string fileName) =>
        File.ReadAllText(Path.Combine(SiteDirectory, "snippets", fileName))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();

    /// <summary>
    /// Asserts that <paramref name="actual"/> is what <paramref name="shown"/> shows, where a
    /// <c>/* … */</c> in the figure stands for emitted text the figure leaves out and has to contain
    /// what it stands for.
    /// </summary>
    private static void AssertShows(string shown, string actual)
    {
        const string Open = "/*";
        const string Close = "*/";

        var open = shown.IndexOf(Open, StringComparison.Ordinal);
        if (open < 0)
        {
            Assert.Equal(shown, actual);
            return;
        }

        var close = shown.IndexOf(Close, open, StringComparison.Ordinal);
        Assert.True(close >= 0, $"The figure line has an unterminated placeholder: {shown}");

        var before = shown[..open];
        var after = shown[(close + Close.Length)..];
        var elided = shown[(open + Open.Length)..close].Trim();

        Assert.True(
            Surrounds(before, after, actual),
            $"figure:  {shown}\nemitted: {actual}");

        var stood = actual[before.Length..^after.Length];
        Assert.True(
            stood.Contains(elided, StringComparison.Ordinal),
            $"The figure elides {elided}, which is not what the generator emitted there.\n\n" +
            $"figure:  {shown}\nemitted: {actual}");
    }

    /// <summary>
    /// Whether <paramref name="text"/> opens with <paramref name="before"/> and closes with
    /// <paramref name="after"/> without the two overlapping, so what lies between them is what they
    /// stand around rather than a slice of one of them. Both figures are compared this way: the fixed
    /// text is the figure's claim, and the remainder is the one thing it leaves to the compiler.
    /// </summary>
    private static bool Surrounds(string before, string after, string text) =>
        text.Length >= before.Length + after.Length
            && text.StartsWith(before, StringComparison.Ordinal)
            && text.EndsWith(after, StringComparison.Ordinal);

    /// <summary>
    /// The text a hand-written figure puts on the page, read out of its source: every string literal in
    /// child position, in order. A literal in a decoration call is a class name and not text, which is
    /// what keeps <c>slab</c>, <c>diag-loc</c> and <c>diag-id</c> out of the result.
    /// </summary>
    private static string RenderedText(string path)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

        var figure = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(static method => method.Identifier.ValueText == "Diagnostic");

        Assert.True(
            figure is not null,
            $"{path} declares no Diagnostic figure. The landing page's build failure moved; point this " +
            "test at wherever it lives now.");

        return string.Concat(figure!.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(static literal => literal.Ancestors()
                .OfType<BaseArgumentListSyntax>()
                .FirstOrDefault() is BracketedArgumentListSyntax)
            .Select(static literal => literal.Token.ValueText));
    }

    /// <summary>The shape a build prints a diagnostic in, which is what the figure imitates.</summary>
    [GeneratedRegex(
        @"^(?<location>[^\n]+): (?<severity>[a-z]+) (?<id>BCF\d{4}):\n(?<message>.+)$",
        RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex BuildError();
}
