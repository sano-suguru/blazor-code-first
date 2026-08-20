using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The type a scalar <c>.Param</c> value carries into generated code. The value expression is
/// transplanted into <c>AddComponentParameter</c>, whose value parameter is <c>object?</c>, so a value
/// that had a target type at the call site arrives with none (#377).
/// </summary>
public sealed class ComponentParameterValueTypeTests
{
    private const string ProbeSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public sealed class Probe : ComponentBase
        {
            [Parameter] public System.Func<int, string>? Formatter { get; set; }
            [Parameter] public System.Predicate<int>? Match { get; set; }
            [Parameter] public string? Label { get; set; }
            [Parameter] public int Count { get; set; }
        }
        """;

    /// <summary>
    /// Runs the generator over a host whose <c>Body</c> is <paramref name="body"/> and returns the
    /// generated text. The output is compiled and swept for warnings, not merely inspected: half of #377 is
    /// generated code that fails to bind, and the cast that closes it warns CS8600 on a value written as
    /// <c>null</c> unless the type name carries the nullable annotation. Neither is visible to an assertion
    /// over the text, and the second reaches a consumer as a build failure they cannot fix.
    /// </summary>
    private static string GenerateBody(string body, string members = "")
    {
        var host = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => {{body}};
                {{members}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Probe.cs", ProbeSource), ("Host.cs", host));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(result);
        return result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
    }

    [Fact]
    public void Param_WithUntypedLambdaValue_CastsToTheResolvedValueType()
    {
        var generated = GenerateBody(
            "Component<Probe>().Param(p => p.Formatter, x => x.ToString())");

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Formatter\", "
                + "(global::System.Func<global::System.Int32, global::System.String>?)(x => x.ToString()));",
            generated);
    }

    [Fact]
    public void Param_WithMethodGroupValue_CastsToTheResolvedValueType()
    {
        var generated = GenerateBody(
            "Component<Probe>().Param(p => p.Formatter, Format)",
            "private string Format(int x) => x.ToString();");

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Formatter\", "
                + "(global::System.Func<global::System.Int32, global::System.String>?)(Format));",
            generated);
    }

    /// <summary>
    /// The half that binds and is the wrong type. A lambda's natural type is <c>Func&lt;int, bool&gt;</c>,
    /// so without the cast the parameter arrives as one and the renderer throws when it assigns.
    /// </summary>
    [Fact]
    public void Param_WithTypedLambdaUnderADeclaredDelegate_CastsToTheDeclaredDelegate()
    {
        var generated = GenerateBody(
            "Component<Probe>().Param(p => p.Match, (int x) => x > 0)");

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Match\", "
                + "(global::System.Predicate<global::System.Int32>?)((int x) => x > 0));",
            generated);
    }

    /// <summary>
    /// One rule, not a delegate-shaped exception to it: the type the call site resolved is written for
    /// every scalar value. Razor splits here, writing the cast only for a delegate and
    /// <c>RuntimeHelpers.TypeCheck&lt;T&gt;</c> otherwise, and the split is not forced by C# — both
    /// spellings bind all of these shapes (measured 2026-08-16).
    /// </summary>
    [Fact]
    public void Param_WithNonDelegateValue_CastsToTheResolvedValueTypeToo()
    {
        var generated = GenerateBody(
            "Component<Probe>().Param(p => p.Label, \"t\").Param(p => p.Count, 3)");

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Label\", (global::System.String?)(\"t\"));",
            generated);
        Assert.Contains(
            "__builder.AddComponentParameter(2, \"Count\", (global::System.Int32)(3));",
            generated);
    }

    /// <summary>
    /// The nullable annotation travels with the name. A cast that drops it warns CS8600 where the value is
    /// written as <c>null</c>, and a warning inside generated code is a build failure for every consumer
    /// building with <c>TreatWarningsAsErrors</c> and one they cannot fix. Found by building
    /// <c>site/</c>, which writes this exact shape.
    /// </summary>
    [Fact]
    public void Param_WithNullLiteralValue_CastsToTheAnnotatedValueType()
    {
        var generated = GenerateBody("Component<Probe>().Param(p => p.Label, null)");

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Label\", (global::System.String?)(null));",
            generated);
    }

    /// <summary>
    /// The resolved type argument and not the declared property type. The two differ only where the author
    /// writes the type argument out, and the resolved one is the type the value was already converted to at
    /// the call site, so the cast can never fail to bind in a file the author cannot reach (Appendix A A.0).
    /// </summary>
    [Fact]
    public void Param_WithWidenedTypeArgument_CastsToTheWrittenTypeArgument()
    {
        var generated = GenerateBody(
            "Component<Probe>().Param<object?>(p => p.Label, _value)",
            "private object? _value;");

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Label\", (global::System.Object?)(_value));",
            generated);
    }
}
