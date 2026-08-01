using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Holds the two design-time-expression hosts to the same set of failure-path scanners.
/// </summary>
/// <remarks>
/// <para>
/// A design-time expression has two hosts, and they are separate code paths that happen to share an
/// analyzer: <c>ComponentModelFactory</c> analyzes a component's <c>Body</c>/<c>Chrome</c>, and
/// <c>ComposableDefinitionFactory</c> analyzes a <c>[Composable]</c> method body.  A check that lives
/// inside <c>RenderExpressionAnalyzer</c> reaches both for free, because both route through it.  A check
/// that lives in a scanner the host invokes reaches only the hosts that invoke it — and that difference is
/// invisible at the call site, because each factory looks complete on its own.
/// </para>
/// <para>
/// That is not hypothetical.  BC3008 was reported from inside <c>RenderExpressionAnalyzer.Classify</c>
/// until it moved to <c>RejectedDecorationScanner</c>; the move wired it into the component host only, and
/// a misplaced decoration inside a <c>[Composable]</c> body silently degraded to the generic BC1002 for the
/// life of the branch.  Every BC3008 test hosted its expression in <c>Body</c>, so nothing failed.  This
/// test is the guard that was missing, and its guarantee has a condition attached: it fails if a scanner
/// named <c>SomethingScanner</c>, declared as its own file under <c>src/BlazorCompose.Compiler/Analysis</c>
/// and invoked as <c>SomethingScanner.Report(…)</c>, is wired into one host and not the other, and it fails
/// on the state that shipped that regression.  The <c>Scanner</c> suffix is load-bearing on both sides of
/// that comparison — <see cref="DeclaredScanners"/> finds a declared scanner by filename and
/// <see cref="ScannersInvokedBy"/> finds an invoked one by identifier, both keyed on the same suffix — so a
/// sweep renamed off that convention (say, <c>RejectedDecorationSweep</c>) drops out of both sets at once,
/// and a one-host wiring under that name would pass here silently.
/// </para>
/// <para>
/// It reads the two factories' source text rather than their behaviour, which is the only way to see a
/// scanner that <em>no</em> test exercises yet.  Be clear about what that cannot see: it does not know
/// whether a call sits on the failure path, whether the two calls are passed equivalent expressions, or
/// whether a scanner is reached indirectly through a helper.  Those are for the behavioural tests — see
/// <c>BracketSurfaceDiagnosticTests.DecoratingANonElement_InsideAComposableBody_ReportsBC3008</c>, which
/// pins the author-facing result for the one scanner this guard was written for.  What it does see is
/// membership, which is exactly the thing that stopped being implied when detection moved out of
/// <c>Classify</c>.
/// </para>
/// </remarks>
public sealed class FailurePathScannerParityTests
{
    /// <summary>The file each host is implemented in, named by the host it analyzes for.</summary>
    private const string ComponentHost = "src/BlazorCompose.Compiler/ComponentModelFactory.cs";
    private const string ComposableHost = "src/BlazorCompose.Compiler/Analysis/ComposableDefinitionFactory.cs";

    /// <summary>Where a scanner is declared, one type per file.</summary>
    private const string ScannerDirectory = "src/BlazorCompose.Compiler/Analysis";

    [Fact]
    public void BothHosts_InvokeTheSameScanners()
    {
        var component = ScannersInvokedBy(ComponentHost);
        var composable = ScannersInvokedBy(ComposableHost);

        // Sorted sequences rather than sets, so the failure message names which side is short.
        Assert.Equal(component, composable);
    }

    [Fact]
    public void EveryDeclaredScanner_IsInvokedByBothHosts()
    {
        var declared = DeclaredScanners();

        // Proves this guard is looking at something before its comparisons are trusted: a renamed directory
        // or a changed file-naming convention would otherwise make every assertion here pass on an empty set.
        Assert.NotEmpty(declared);

        Assert.Equal(declared, ScannersInvokedBy(ComponentHost));
        Assert.Equal(declared, ScannersInvokedBy(ComposableHost));
    }

    // ---------------------------------------------------------------------------

    /// <summary>
    /// Every <c>SomethingScanner.Report(…)</c> written in <paramref name="relativePath"/>, sorted and
    /// deduplicated.
    /// </summary>
    /// <remarks>
    /// Matched on the syntax rather than on a regex over the text so a scanner named in a comment or an XML
    /// doc — both files carry several — is not counted as a call.  A qualified receiver
    /// (<c>Analysis.RejectedDecorationScanner</c>) is read from its final name segment, so adding a
    /// qualification does not read as removing the call.
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
            .Where(static name => name is not null && name.EndsWith("Scanner", StringComparison.Ordinal))
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)];
    }

    /// <summary>Every scanner type the compiler declares, taken from the one-type-per-file layout.</summary>
    private static IReadOnlyList<string> DeclaredScanners()
    {
        var directory = Path.Combine(
            GeneratedSourceSnapshot.FindRepositoryRoot(),
            ScannerDirectory.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            Directory.Exists(directory),
            $"{ScannerDirectory} does not exist. The scanners moved; point this guard at wherever they live " +
            "now, rather than letting it compare two empty sets.");

        return [.. Directory.EnumerateFiles(directory, "*Scanner.cs")
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(static name => name, StringComparer.Ordinal)];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = Path.Combine(
            GeneratedSourceSnapshot.FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            File.Exists(path),
            $"{relativePath} does not exist. A design-time-expression host was renamed or moved; update this " +
            "guard to name it, because a host it cannot find is a host it cannot check.");

        return File.ReadAllText(path);
    }
}
