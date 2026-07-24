using System.Threading.Tasks;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Tests for <see cref="RenderMutationAnalyzer"/> (BC3001).
/// Verifies that direct state mutations in the Body rendering path are diagnosed,
/// while mutations inside recognized deferred event handler lambdas are not.
/// </summary>
public sealed class RenderMutationAnalyzerTests
{
    // -----------------------------------------------------------------------
    // Sources that should report BC3001
    // -----------------------------------------------------------------------

    private const string IncrementInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count++}");
        }
        """;

    private const string AssignmentInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count = 4}");
        }
        """;

    private const string CompoundAssignmentInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count += 4}");
        }
        """;

    private const string DecrementInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count--}");
        }
        """;

    private const string PropertyAssignmentInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Text($"{Count = 4}");
        }
        """;

    private const string PropertyIncrementInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Text($"{Count++}");
        }
        """;

    /// <summary>
    /// Negative control for the Html.OnClick exemption: the mutation's nearest enclosing lambda is
    /// the Html.If content lambda, not an OnClick handler, so it must still report BC3001.
    /// </summary>
    private const string IncrementInHtmlIfContentLambdaSource = """
        using BlazorCompose;

        public partial class Counter : ComposeComponentBase
        {
            private bool _flag = true;
            private int _count;
            protected override View Body => Html.If(_flag, () => Html.Span((_count++).ToString()));
        }
        """;

    // -----------------------------------------------------------------------
    // Sources that must NOT report BC3001
    // -----------------------------------------------------------------------

    private const string IncrementInButtonHandlerSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Button("Increment", () => _count++);
        }
        """;

    private const string PropertyIncrementInButtonHandlerSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Button("Increment", () => Count++);
        }
        """;

    private const string HelperMutationSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;

            protected override View Body => Text(MutateAndReturnText());

            private string MutateAndReturnText()
            {
                _count++;
                return _count.ToString();
            }
        }
        """;

    /// <summary>
    /// Positive case for the Html.OnClick exemption: the mutation's nearest enclosing lambda is the
    /// (reduced) sole argument of the Html-mirror <c>View.OnClick(...)</c> extension call, so it must
    /// not report BC3001.
    /// </summary>
    private const string IncrementInHtmlOnClickHandlerSource = """
        using BlazorCompose;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Html.Button("Increment").OnClick(() => _count++);
        }
        """;

    // -----------------------------------------------------------------------
    // Theory data
    // -----------------------------------------------------------------------

    /// <summary>Body-path mutations that must each be diagnosed as a BC3001 error.</summary>
    public static TheoryData<string> MutationSourcesThatReportBC3001 { get; } = BuildTheoryData(
        IncrementInTextSource,
        AssignmentInTextSource,
        CompoundAssignmentInTextSource,
        DecrementInTextSource,
        PropertyAssignmentInTextSource,
        PropertyIncrementInTextSource,
        IncrementInHtmlIfContentLambdaSource);

    /// <summary>Deferred mutations (event handlers, helper methods) that must not report BC3001.</summary>
    public static TheoryData<string> MutationSourcesThatDoNotReportBC3001 { get; } = BuildTheoryData(
        IncrementInButtonHandlerSource,
        PropertyIncrementInButtonHandlerSource,
        HelperMutationSource,
        IncrementInHtmlOnClickHandlerSource);

    private static TheoryData<string> BuildTheoryData(params string[] sources)
    {
        var data = new TheoryData<string>();

        foreach (var source in sources)
        {
            data.Add(source);
        }

        return data;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(MutationSourcesThatReportBC3001))]
    public async Task RenderMutationAnalyzer_MutationInBodyRenderPath_ReportsBC3001(string source)
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(MutationSourcesThatDoNotReportBC3001))]
    public async Task RenderMutationAnalyzer_DeferredMutation_DoesNotReportBC3001(string source)
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }
}
