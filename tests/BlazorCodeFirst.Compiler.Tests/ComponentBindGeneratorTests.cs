using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The component surface's <c>.Bind</c>, whose parameter names are derived from the selector rather than
/// written by the author. Every case here is about a derivation that has to be checked against
/// <c>TComponent</c>, which is what makes the derivation admissible at all.
/// </summary>
public sealed class ComponentBindGeneratorTests
{
    private const string Probe = """
        public sealed class Probe : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter] public string Value { get; set; } = "";
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.EventCallback<string> ValueChanged { get; set; }
        }
        """;

    private const string ProbeWithExpression = """
        public sealed class ProbeWithExpression : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter] public string Value { get; set; } = "";
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.EventCallback<string> ValueChanged { get; set; }
            [Microsoft.AspNetCore.Components.Parameter]
            public System.Linq.Expressions.Expression<System.Func<string>>? ValueExpression { get; set; }
        }
        """;

    private const string ProbeWithoutChanged = """
        public sealed class ProbeWithoutChanged : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter] public string Value { get; set; } = "";
        }
        """;

    private const string ProbeWithMistypedChanged = """
        public sealed class ProbeWithMistypedChanged : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter] public string Value { get; set; } = "";
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.EventCallback<int> ValueChanged { get; set; }
        }
        """;

    /// <summary>
    /// Runs the generator over <paramref name="body"/> placed in a component that binds against
    /// <paramref name="probe"/>, and returns the generated text. The output is compiled, not merely
    /// inspected: the change callback and the field expression are both written with casts the generated
    /// file has no <c>using</c> for, so a spelling that reads correctly can still fail to bind.
    /// </summary>
    private static string GenerateBodyWith(string probe, string body)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Probe.cs", probe), ("Host.cs", source));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
        return result.GeneratedSources.Single().SourceText.ToString();
    }

    private static void AssertDiagnosticWith(string probe, string body, string id)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Probe.cs", probe), ("Host.cs", source));
        Assert.Contains(result.Diagnostics, d => d.Id == id);
    }

    /// <summary>
    /// Like <see cref="AssertDiagnosticWith"/>, plus asserting BCF3015 is absent: the guard under test
    /// must be the one that suppresses the unresolved-type reference <paramref name="body"/> carries,
    /// not the generic <c>UnresolvedValueTypeScanner</c> fallback (#487).
    /// </summary>
    private static void AssertDiagnosticWithoutBcf3015(string probe, string body, string id)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Probe.cs", probe), ("Host.cs", source));
        Assert.Contains(result.Diagnostics, d => d.Id == id);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3015");
    }

    [Fact]
    public void Bind_ComponentWithoutValueExpression_EmitsTwoParameters()
    {
        var generated = GenerateBodyWith(Probe, """
            private string _name = "";
            protected override View Body => Html.Component<Probe>().Bind(c => c.Value, () => _name);
            """);

        Assert.Contains("__builder.AddComponentParameter(1, \"Value\", _name);", generated);
        Assert.Contains(
            "__builder.AddComponentParameter(2, \"ValueChanged\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>("
            + "this, (global::System.Action<global::System.String>)(__value => _name = __value)));",
            generated);
        Assert.DoesNotContain("ValueExpression", generated);
    }

    [Fact]
    public void Bind_ComponentDeclaringValueExpression_EmitsThreeParameters()
    {
        var generated = GenerateBodyWith(ProbeWithExpression, """
            private string _name = "";
            protected override View Body =>
                Html.Component<ProbeWithExpression>().Bind(c => c.Value, () => _name);
            """);

        Assert.Contains(
            "__builder.AddComponentParameter(3, \"ValueExpression\", "
            + "(global::System.Linq.Expressions.Expression<global::System.Func<global::System.String>>)"
            + "(() => _name));",
            generated);
    }

    [Fact]
    public void Bind_ExplicitSyncSetter_TransplantsAuthorLambdaCastToAction()
    {
        var generated = GenerateBodyWith(Probe, """
            private string Query { get; set; } = "";
            protected override View Body =>
                Html.Component<Probe>().Bind(c => c.Value, () => Query, v => Query = v.Trim());
            """);

        Assert.Contains(
            "__builder.AddComponentParameter(2, \"ValueChanged\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>("
            + "this, (global::System.Action<global::System.String>)(v => Query = v.Trim())));",
            generated);
    }

    [Fact]
    public void Bind_ExplicitAsyncSetter_CastsToFuncOfTask()
    {
        // No RuntimeHelpers.CreateInferredBindSetter here, unlike the element surface: CreateBinder takes
        // an Action-shaped setter and has to have the asynchronous one adapted, while EventCallback's
        // Create declares a Func<T, Task> overload of its own.
        var generated = GenerateBodyWith(Probe, """
            private string _name = "";
            private System.Threading.Tasks.Task SetAsync(string v)
            {
                _name = v;
                return System.Threading.Tasks.Task.CompletedTask;
            }
            protected override View Body =>
                Html.Component<Probe>().Bind(c => c.Value, () => _name, SetAsync);
            """);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>("
            + "this, (global::System.Func<global::System.String, "
            + "global::System.Threading.Tasks.Task>)(SetAsync))",
            generated);
        Assert.DoesNotContain("CreateInferredBindSetter", generated);
    }

    [Fact]
    public void Bind_InsideViewPart_SubstitutesBothTheValueAndTheChangeCallback()
    {
        // Why the change callback is composed from the getter's segments rather than from its ToCode():
        // inside a [ViewPart] body the getter still holds an unbound parameter hole, and ToCode() throws
        // on those. The same hole must be filled on both sides, or the parameter and its callback would
        // name different objects.
        var generated = GenerateBodyWith(Probe, """
            private sealed class FormModel { public string Name { get; set; } = ""; }
            private readonly FormModel _form = new();

            [ViewPart]
            private static View Field(FormModel model) =>
                Html.Component<Probe>().Bind(c => c.Value, () => model.Name);

            protected override View Body => Html.Div[Field(_form)];
            """);

        Assert.Contains("\"Value\", __bcf_arg_1_0.Name);", generated);
        Assert.Contains("(__value => __bcf_arg_1_0.Name = __value)", generated);
    }

    [Fact]
    public void Bind_ComponentWithoutChangeCallback_ReportsBcf3020()
    {
        AssertDiagnosticWith(ProbeWithoutChanged, """
            private string _name = "";
            protected override View Body =>
                Html.Component<ProbeWithoutChanged>().Bind(c => c.Value, () => _name);
            """, "BCF3020");
    }

    [Fact]
    public void Bind_ChangeCallbackOfAnotherType_ReportsBcf3020()
    {
        // The derived name is present, so only the type check stands between this and a generated call
        // that would not compile. A name-only lookup would accept it.
        AssertDiagnosticWith(ProbeWithMistypedChanged, """
            private string _name = "";
            protected override View Body =>
                Html.Component<ProbeWithMistypedChanged>().Bind(c => c.Value, () => _name);
            """, "BCF3020");
    }

    [Fact]
    public void Bind_BesideParamOnChangeCallback_ReportsBcf3007()
    {
        AssertDiagnosticWith(Probe, """
            private string _name = "";
            protected override View Body =>
                Html.Component<Probe>()
                    .Param(c => c.ValueChanged, default(Microsoft.AspNetCore.Components.EventCallback<string>))
                    .Bind(c => c.Value, () => _name);
            """, "BCF3007");
    }

    [Fact]
    public void Bind_NonAssignableGetter_ReportsBcf3018()
    {
        AssertDiagnosticWith(Probe, """
            private string _name = "";
            protected override View Body =>
                Html.Component<Probe>().Bind(c => c.Value, () => _name.Trim());
            """, "BCF3018");
    }

    [Fact]
    public void Bind_InitOnlyGetter_ReportsBcf3018()
    {
        // Both surfaces reach BindTargetResolver, so the shapes whose setter exists but cannot be called
        // are rejected here too. Asserted on one of the two, since a second copy of every case would only
        // restate that the resolver is shared (#391).
        AssertDiagnosticWith(Probe, """
            public string Name { get; init; } = "";
            protected override View Body =>
                Html.Component<Probe>().Bind(c => c.Value, () => Name);
            """, "BCF3018");
    }

    /// <summary>
    /// The unresolved reference goes through a plain helper call rather than a bare value inside the
    /// getter's lambda body: a bare value there would poison <c>FactoryArguments.Bind</c> for the whole
    /// invocation before the duplicate check is ever reached (#487). The duplicate check runs before the
    /// getter argument is read at all, so a helper-call getter carries the payload here regardless of its
    /// own shape.
    /// </summary>
    [Fact]
    public void Bind_BesideParamOnChangeCallback_WithUnresolvedTypeArgument_ReportsBcf3007AndNotBcf3015()
    {
        AssertDiagnosticWithoutBcf3015(Probe, """
            private static System.Func<string> MakeGetter(string s) => () => s;
            protected override View Body =>
                Html.Component<Probe>()
                    .Param(c => c.ValueChanged, default(Microsoft.AspNetCore.Components.EventCallback<string>))
                    .Bind(c => c.Value, MakeGetter(typeof(Unresolved).Name));
            """, "BCF3007");
    }

    /// <summary>
    /// Same helper-call construction as the duplicate-check probe above: the assignability check also
    /// runs before the getter argument is read, so its own shape is free to carry the payload.
    /// </summary>
    [Fact]
    public void Bind_ComponentWithoutChangeCallback_WithUnresolvedTypeArgument_ReportsBcf3020AndNotBcf3015()
    {
        AssertDiagnosticWithoutBcf3015(ProbeWithoutChanged, """
            private static System.Func<string> MakeGetter(string s) => () => s;
            protected override View Body =>
                Html.Component<ProbeWithoutChanged>().Bind(c => c.Value, MakeGetter(typeof(Unresolved).Name));
            """, "BCF3020");
    }

    /// <summary>
    /// The same helper-call getter that carries the payload past the two guards above is, here, the very
    /// shape this guard exists to reject: a getter argument that is not an inline lambda at all.
    /// </summary>
    [Fact]
    public void Bind_NonLambdaGetter_WithUnresolvedTypeArgument_ReportsBcf3017AndNotBcf3015()
    {
        AssertDiagnosticWithoutBcf3015(Probe, """
            private static System.Func<string> MakeGetter(string s) => () => s;
            protected override View Body =>
                Html.Component<Probe>().Bind(c => c.Value, MakeGetter(typeof(Unresolved).Name));
            """, "BCF3017");
    }
}
