using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds every position that unwraps a lambda to the list this file names, and every transplanting one
/// among them to the reserved-name scan.
/// </summary>
/// <remarks>
/// <para>
/// The failure mode this descends from: <c>DeclaresReservedName</c> stops its walk at a lambda, because a
/// lambda's body is not the enclosing position's to transplant. The position that accepts the lambda has to
/// answer for that body itself, and the rule saying so lived only in that method's remarks. #389 left two
/// positions not asking, #412 added the <c>If</c> branches and still left them, and a third was found
/// beside them (#413). Prose was not holding it, so this reads the source.
/// </para>
/// <para>
/// Keyed on the helpers that hand a body to a position rather than on a name convention: a position gets a
/// lambda's body through one of them, so a new position appears here whether or not whoever wrote it knew
/// about this guard. <see cref="LambdaSyntaxIsNamedOnlyWhereALambdaIsUnwrappedOrSkipped"/> covers the
/// remaining way in, an unwrap written out by hand rather than through a helper.
/// </para>
/// <para>
/// One of those helpers, <c>TryBindTransplantedLambda</c>, runs the scan itself, so a position reading
/// through it satisfies both halves at once. That is what it is for: a position cannot take a transplanted
/// body from it and skip the question. This guard checks that shape rather than being the only thing
/// holding it.
/// </para>
/// <para>
/// It reads source text rather than behaviour, which is the only way to see a position that <em>no</em>
/// test exercises yet. What it cannot see is whether the scan is asked about the right node, or asked
/// before the body is used: a position could call it on an unrelated expression and pass here. That is for
/// the behavioural tests, one per position:
/// <c>ForEachTransplantTests.ForEachKey_WhenItDeclaresAReservedName_ReportsBCF3004</c>,
/// <c>BracketSurfaceDiagnosticTests.SpreadOfAProjection_WhenItDeclaresAReservedName_ReportsBCF1003</c>,
/// <c>ComponentTemplateDiagnosticTests.ContextualTemplate_WhenItDeclaresAReservedName_ReportsBCF3022</c>,
/// and the <c>ForEachContent</c> and <c>If</c> cases beside the first two.
/// </para>
/// <para>
/// Scoped to one file because the classifier is one file. A position that moves out of it drops out of
/// both sets at once and this guard goes quiet, which is why <see cref="ReadAnalyzer"/> fails loudly on a
/// path it cannot find and why the census asserts a non-empty result before comparing.
/// </para>
/// </remarks>
public sealed class LambdaUnwrapPositionTests
{
    private const string Analyzer = "src/BlazorCodeFirst.Compiler/Analysis/RenderExpressionAnalyzer.cs";

    /// <summary>The helpers a position takes a lambda's body from.</summary>
    private static readonly string[] UnwrapHelpers =
    [
        "ExtractLambdaBody",
        "TryBindTransplantedLambda",
        "TryExtractLambdaParameterAndBody",
        "TryExtractSingleParameterLambda",
    ];

    /// <summary>
    /// What a position answers the reserved names with. <c>TryReadTransplantableBlock</c>,
    /// <c>TryReadTransplantableIf</c>, <c>TryReadTransplantableSwitch</c>, and
    /// <c>TryBindTransplantedLambda</c> count because each runs the scan over what it accepts, which is
    /// how the block-bodied <c>ForEach</c> content (all three Transplantable shapes, #570) and the three
    /// lambda-reading positions ask.
    /// </summary>
    private static readonly string[] Scans =
    [
        "DeclaresReservedName",
        "TryBindTransplantedLambda",
        "TryReadTransplantableBlock",
        "TryReadTransplantableIf",
        "TryReadTransplantableSwitch",
    ];

    /// <summary>
    /// Every position that unwraps a lambda, how many it unwraps, and whether the body it takes is
    /// transplanted under the author's own names — which is what decides whether it must ask the scan.
    /// </summary>
    /// <remarks>
    /// A new position fails <see cref="EveryUnwrapPosition_IsOneThisTableNames"/> until it is written here,
    /// and writing it here is the decision the failure asks for: transplanted, so it asks the scan, or not,
    /// with the reason beside the row.
    /// </remarks>
    public static TheoryData<string, int, bool> Positions() => new()
    {
        // The projection of a spliced `.. source.Select(item => …)`, transplanted into the folded loop.
        // Extracted from AnalyzeSplice into its own method so AnalyzeSplice can also try the iterator-
        // [ViewPart]-call spread shape when the projection syntax does not match (#316).
        { "AnalyzeSplicedProjection", 1, true },
        // Both branches, each transplanted into one arm of the generated `if`.
        { "ClassifyIf", 2, true },
        // The content of a contextual `.Template`, transplanted into the generated fragment. The other
        // arms of this dispatcher unwrap nothing, which is why one row covers it.
        { "ClassifyComponentParameter", 1, true },
        // The content of a `ForEach`, transplanted into the generated loop in either body form.
        { "TryBindForEachContent", 1, true },
        // The key of a `ForEach`, transplanted into `SetKey`.
        { "TryBindForEachKey", 1, true },
        // Not transplanted: the body has to be `p.Property`, a member access on the lambda's own
        // parameter, and every other shape is refused before anything is read from it. Nothing can be
        // declared in a member access, so there is no name for the scan to find.
        { "TryGetSelectorProperty", 1, false },
    };

    /// <summary>
    /// The members that may name a lambda syntax type, and why each does.
    /// </summary>
    /// <remarks>
    /// The three unwrap helpers name one because unwrapping is what they do. The two walks name
    /// <c>AnonymousFunctionExpressionSyntax</c> as the boundary they stop at, which is the same rule read
    /// from the other side. <c>TryBindForEachContent</c> names one to separate a lambda from a method
    /// group before it unwraps, a question no helper answers for it.
    /// </remarks>
    private static readonly string[] MembersThatMayNameLambdaSyntax =
    [
        "CollectDeclaredLocals",
        "DeclaresReservedName",
        "ExtractLambdaBody",
        "TryBindForEachContent",
        "TryExtractLambdaParameterAndBody",
    ];

    [Fact]
    public void EveryUnwrapPosition_IsOneThisTableNames()
    {
        var found = UnwrapsByPosition();

        // Proves this guard is looking at something before its comparison is trusted: a renamed helper
        // would otherwise make the assertion pass on two empty sets.
        Assert.NotEmpty(found);

        List<string> expected = [.. Positions()
            .Select(static row => $"{(string)row[0]} x{(int)row[1]}")
            .OrderBy(static entry => entry, StringComparer.Ordinal)];

        // Sorted sequences rather than sets, so the failure message names which side is short.
        List<string> actual = [.. found
            .Select(static entry => $"{entry.Key} x{entry.Value}")
            .OrderBy(static entry => entry, StringComparer.Ordinal)];

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void EveryTransplantingUnwrapPosition_AsksTheReservedNameScan(
        string position, int unwraps, bool transplants)
    {
        // How many lambdas the position unwraps is the census assertion's column, not this one's; the
        // parameter is declared because the table feeds both.
        _ = unwraps;

        if (!transplants)
            return;

        var method = Assert.Single(
            MethodsIn(ReadAnalyzer()), m => m.Identifier.ValueText == position);

        Assert.True(
            CallsIn(method).Any(name => Scans.Contains(name, StringComparer.Ordinal)),
            $"{position} unwraps a lambda and transplants its body, but asks neither "
            + $"{string.Join(" nor ", Scans)}. A local the author spelled with the generator's own prefix, "
            + "or the builder's name, would reach a generated file they cannot edit (#413).");
    }

    [Fact]
    public void EveryReaderTrustedAsAScan_RunsTheScanItself()
    {
        // A position satisfies the assertion above by calling a reader rather than the scan, so a reader
        // that stopped running the scan would take every position it serves quiet with it — the assertion
        // would keep passing while nothing was checked. This is what stops that.
        var methods = MethodsIn(ReadAnalyzer()).ToList();

        foreach (var reader in Scans.Where(static scan => scan != "DeclaresReservedName"))
        {
            var method = Assert.Single(methods, m => m.Identifier.ValueText == reader);

            Assert.True(
                CallsIn(method).Contains("DeclaresReservedName", StringComparer.Ordinal),
                $"{reader} is trusted as a scan by EveryTransplantingUnwrapPosition_AsksTheReservedNameScan "
                + "but does not run DeclaresReservedName, so every position reading through it is "
                + "unchecked while that assertion still passes.");
        }
    }

    [Fact]
    public void LambdaSyntaxIsNamedOnlyWhereALambdaIsUnwrappedOrSkipped()
    {
        // The way in that the census above cannot see: a position matching a lambda by hand instead of
        // calling a helper unwraps one without appearing in either set.
        var named = MethodsIn(ReadAnalyzer())
            .Where(static method => method.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(static name => IsLambdaSyntaxType(name.Identifier.ValueText)))
            .Select(static method => method.Identifier.ValueText)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(named);
        Assert.Equal(MembersThatMayNameLambdaSyntax.OrderBy(static name => name, StringComparer.Ordinal), named);
    }

    // ---------------------------------------------------------------------------

    /// <summary>
    /// How many lambdas each method of the analyzer unwraps, counting only methods that unwrap one.
    /// </summary>
    /// <remarks>
    /// The helpers' own calls to one another are not positions and are excluded by name, which is why
    /// <c>TryExtractSingleParameterLambda</c> delegating to <c>TryExtractLambdaParameterAndBody</c>, and
    /// <c>TryBindTransplantedLambda</c> delegating to both, do not read as positions of their own.
    /// </remarks>
    private static Dictionary<string, int> UnwrapsByPosition()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var method in MethodsIn(ReadAnalyzer()))
        {
            var name = method.Identifier.ValueText;
            if (UnwrapHelpers.Contains(name, StringComparer.Ordinal))
                continue;

            var unwraps = CallsIn(method).Count(call => UnwrapHelpers.Contains(call, StringComparer.Ordinal));
            if (unwraps > 0)
                counts[name] = unwraps;
        }

        return counts;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is one of the syntax types a lambda is matched through. Matched on
    /// the suffix rather than on a fixed list, so a spelling Roslyn adds later is caught rather than
    /// silently admitted.
    /// </summary>
    private static bool IsLambdaSyntaxType(string name) =>
        name.EndsWith("LambdaExpressionSyntax", StringComparison.Ordinal)
        || name is "AnonymousFunctionExpressionSyntax" or "AnonymousMethodExpressionSyntax";

    private static IEnumerable<MethodDeclarationSyntax> MethodsIn(SyntaxNode root) =>
        root.DescendantNodes().OfType<MethodDeclarationSyntax>();

    /// <summary>
    /// The name of every method invoked in <paramref name="method"/>, qualified or not, read from the final
    /// name segment so adding a qualification does not read as removing the call.
    /// </summary>
    private static IEnumerable<string> CallsIn(MethodDeclarationSyntax method) =>
        method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(static invocation => invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
                _ => null,
            })
            .Where(static name => name is not null)
            .Select(static name => name!);

    private static SyntaxNode ReadAnalyzer()
    {
        var path = Path.Combine(
            GeneratedSourceSnapshot.FindRepositoryRoot(),
            Analyzer.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            File.Exists(path),
            $"{Analyzer} does not exist. The classifier was renamed or moved; point this guard at wherever "
            + "it lives now, because a file it cannot find is a file it cannot check.");

        return CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
    }
}
