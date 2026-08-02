using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Holds every failure-path scanner to the single list both design-time-expression hosts run.
/// </summary>
/// <remarks>
/// <para>
/// The failure mode this descends from: BC3008 moved from <c>RenderExpressionAnalyzer.Classify</c> — which
/// both hosts route through — into a caller-invoked sweep, was wired into the component host only, and a
/// misplaced decoration inside a <c>[Composable]</c> body degraded to the generic BC1002 for the life of
/// the branch.  The guard that replaced it compared the two hosts' scanner lists.  That comparison is gone
/// because the lists are gone: <c>FailurePathScanners.ReportAll</c> holds one list and both hosts call it,
/// so one-host wiring is no longer something that can be written.
/// </para>
/// <para>
/// What remains to check is narrower — that a declared scanner is actually on that list, and that neither
/// host has grown a direct call around it — and it is keyed on structure rather than on a name.  The old
/// guard keyed on the <c>Scanner</c> suffix on both sides, so a sweep renamed to <c>…Sweep</c> dropped out
/// of both sets at once and a one-host wiring under that name passed silently.  Here the declared side is
/// found by the <c>Report</c> signature, which a rename cannot slip out of: both hosts build a
/// <c>ComposableBodyContext</c>, so any sweep reachable from both necessarily takes one.
/// </para>
/// <para>
/// It reads source text rather than behaviour, which is the only way to see a scanner that <em>no</em>
/// test exercises yet.  It cannot see whether a call sits on the failure path or whether the expressions
/// passed are equivalent; those are for the behavioural tests — see
/// <c>BracketSurfaceDiagnosticTests.DecoratingANonElement_InsideAComposableBody_ReportsBC3008</c>,
/// <c>UnresolvedValueType_InsideAComposableBody_ReportsBC3015</c> and
/// <c>ComponentUnresolvedTypeTests.Component_InsideComposableBody_ReportsBC3012NotGenericBC1002</c>, which
/// are the per-scanner author-facing cases for the <c>[Composable]</c> host.
/// </para>
/// </remarks>
public sealed class FailurePathScannerWiringTests
{
    private const string Aggregator = "src/BlazorCompose.Compiler/Analysis/FailurePathScanners.cs";
    private const string ComponentHost = "src/BlazorCompose.Compiler/ComponentModelFactory.cs";
    private const string ComposableHost = "src/BlazorCompose.Compiler/Analysis/ComposableDefinitionFactory.cs";

    /// <summary>Where a scanner is declared, one type per file.</summary>
    private const string ScannerDirectory = "src/BlazorCompose.Compiler/Analysis";

    [Fact]
    public void EveryDeclaredScanner_IsInvokedByReportAll()
    {
        var declared = DeclaredScanners();

        // Proves this guard is looking at something before its comparison is trusted: a moved directory or
        // a changed Report signature would otherwise make the assertion pass on two empty sets.
        Assert.NotEmpty(declared);

        // Sorted sequences rather than sets, so the failure message names which side is short.
        Assert.Equal(declared, ScannersInvokedBy(Aggregator));
    }

    [Theory]
    [InlineData(ComponentHost)]
    [InlineData(ComposableHost)]
    public void EachHost_RunsTheSweepsThroughReportAll(string host)
    {
        var root = CSharpSyntaxTree.ParseText(ReadRepositoryFile(host)).GetRoot();

        var reportAllCalls = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static invocation =>
                invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ReportAll" });

        Assert.Equal(1, reportAllCalls);

        // A direct scanner call beside ReportAll is how per-host lists would grow back, one call at a time.
        Assert.Empty(ScannersInvokedBy(host));
    }

    // ---------------------------------------------------------------------------

    /// <summary>
    /// Every type under <see cref="ScannerDirectory"/> declaring a failure-path <c>Report</c>, sorted.
    /// </summary>
    /// <remarks>
    /// Keyed on the signature rather than on the filename or the type name, so renaming a sweep off the
    /// <c>…Scanner</c> convention does not remove it from this set.  <c>FailurePathScanners</c> itself
    /// declares <c>ReportAll</c>, not <c>Report</c>, so it does not appear here.
    /// </remarks>
    private static List<string> DeclaredScanners()
    {
        var directory = Path.Combine(
            GeneratedSourceSnapshot.FindRepositoryRoot(),
            ScannerDirectory.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            Directory.Exists(directory),
            $"{ScannerDirectory} does not exist. The scanners moved; point this guard at wherever they " +
            "live now, rather than letting it compare two empty sets.");

        var declared = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            declared.AddRange(root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(static type => type.Members.OfType<MethodDeclarationSyntax>().Any(IsFailurePathReport))
                .Select(static type => type.Identifier.ValueText));
        }

        declared.Sort(StringComparer.Ordinal);
        return declared;
    }

    /// <summary>
    /// Whether <paramref name="method"/> is <c>public static void Report(ExpressionSyntax, ComposableBodyContext)</c>.
    /// </summary>
    /// <remarks>
    /// Matched on the written syntax, not on resolved symbols, because this guard's whole point is to see
    /// a scanner before any test exercises it.  The parameter types are compared as written; every scanner
    /// in this repository spells them unqualified.
    /// </remarks>
    private static bool IsFailurePathReport(MethodDeclarationSyntax method) =>
        method.Identifier.ValueText == "Report"
        && method.Modifiers.Any(SyntaxKind.PublicKeyword)
        && method.Modifiers.Any(SyntaxKind.StaticKeyword)
        && method.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
        && method.ParameterList.Parameters is
        [
        { Type: IdentifierNameSyntax { Identifier.ValueText: "ExpressionSyntax" } },
        { Type: IdentifierNameSyntax { Identifier.ValueText: "ComposableBodyContext" } },
        ];

    /// <summary>
    /// Every <c>Something.Report(…)</c> written in <paramref name="relativePath"/>, sorted and deduplicated.
    /// </summary>
    /// <remarks>
    /// Matched on the syntax rather than on a regex over the text, so a scanner named in a comment or an
    /// XML doc is not counted as a call.  A qualified receiver (<c>Analysis.RejectedDecorationScanner</c>)
    /// is read from its final name segment, so adding a qualification does not read as removing the call.
    /// No suffix filter: the receiver name is whatever is written, which is what makes a renamed sweep
    /// still visible here.
    /// </remarks>
    private static IReadOnlyList<string> ScannersInvokedBy(string relativePath)
    {
        var root = CSharpSyntaxTree.ParseText(ReadRepositoryFile(relativePath)).GetRoot();

        return [.. root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(static invocation => invocation.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Where(static access => access.Name.Identifier.ValueText == "Report")
            .Select(static access => access.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.ValueText,
                _ => null,
            })
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = Path.Combine(
            GeneratedSourceSnapshot.FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            File.Exists(path),
            $"{relativePath} does not exist. A design-time-expression host or the sweep list was renamed " +
            "or moved; update this guard to name it, because a file it cannot find is a file it cannot check.");

        return File.ReadAllText(path);
    }
}
