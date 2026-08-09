using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds <see cref="KnownSymbols.TryGetBindParameters"/> against the referenced runtime's own
/// <c>Bind</c> overloads, and against two probe shapes the runtime does not declare.
/// </summary>
/// <remarks>
/// Measured against the real, non-generic runtime surface rather than an in-source declaration, so
/// <c>ValueType</c> is a concrete type and not an unsubstituted type parameter. The probes below are
/// user-declared extension methods in the same compilation: the helper answers from shape and never
/// asks whether the method was classified, so handing it one is legitimate.
/// </remarks>
public sealed class BindParametersTests
{
    private const string Probes = """
        using BlazorCodeFirst;

        public sealed class Probe : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter] public string Value { get; set; } = "";
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.EventCallback<string> ValueChanged { get; set; }
            [Microsoft.AspNetCore.Components.Parameter] public Probe? Self { get; set; }
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.EventCallback<Probe> SelfChanged { get; set; }
        }

        public static class ShapeProbes
        {
            // A zero-argument Action written ahead of the getter. Without the ReturnsVoid clause this
            // parameter is read as the getter.
            public static ElementBuilder ActionAhead(
                this ElementBuilder element, string a, string e,
                System.Action before, System.Func<string> get, System.Action<string> set) => element;

            // A one-argument delegate after the getter that does not take the bound value.
            public static ElementBuilder MistypedSetter(
                this ElementBuilder element, string a, string e,
                System.Func<string> get, System.Action<int> set) => element;
        }

        public class Host
        {
            private string _s = "";
            private bool _b;
            private Probe? _p;

            private System.Threading.Tasks.Task SetAsync(string v) =>
                System.Threading.Tasks.Task.CompletedTask;

            public View Subject => {{EXPRESSION}};
        }
        """;

    /// <summary>Compiles <paramref name="expression"/> and returns the named invocation's resolved
    /// symbol (as <c>GetSymbolInfo</c> answers it) alongside its operation (whose argument parameters
    /// come from the unreduced <c>TargetMethod</c>).</summary>
    private static (IMethodSymbol Method, IInvocationOperation Operation) Resolve(
        string expression, string methodName = "Bind")
    {
        var compilation = CompilationTestHost.CreateCompilation(
            Probes.Replace("{{EXPRESSION}}", expression));
        var tree = compilation.SyntaxTrees[0];
        var model = compilation.GetSemanticModel(tree);

        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(node => model.GetSymbolInfo(node).Symbol is IMethodSymbol method
                && method.Name == methodName);

        var method = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;
        var operation = (IInvocationOperation)model.GetOperation(invocation)!;
        return (method, operation);
    }

    private static BindParameters Bind(string expression, string methodName = "Bind")
    {
        var (method, _) = Resolve(expression, methodName);
        Assert.True(KnownSymbols.TryGetBindParameters(method, out var bind));
        return bind;
    }

    /// <summary>The parameter the argument whose text is <paramref name="argumentText"/> binds to,
    /// taken from the operation exactly as <c>RenderMutationAnalyzer.GetBoundParameter</c> takes it.</summary>
    private static IParameterSymbol BoundParameter(IInvocationOperation operation, string argumentText)
    {
        var argument = operation.Arguments.Single(a => a.Syntax.ToString() == argumentText);
        Assert.NotNull(argument.Parameter);
        return argument.Parameter!;
    }

    [Fact]
    public void ElementGetterOnly_DeclaresNoSetter()
    {
        var bind = Bind("""Html.Input.Bind("value", "oninput", () => _s)""");

        Assert.Equal(2, bind.GetterIndex);
        Assert.Equal(-1, bind.SetterIndex);
        Assert.Equal(SpecialType.System_String, bind.ValueType.SpecialType);
    }

    [Fact]
    public void ElementSynchronousSetter_IsNotAsynchronous()
    {
        var bind = Bind("""Html.Input.Bind("value", "oninput", () => _s, v => _s = v)""");

        Assert.Equal(2, bind.GetterIndex);
        Assert.Equal(3, bind.SetterIndex);
        Assert.False(bind.SetterIsAsynchronous);
    }

    [Fact]
    public void ElementAsynchronousSetter_IsAsynchronous()
    {
        var bind = Bind("""Html.Input.Bind("value", "oninput", () => _s, SetAsync)""");

        Assert.Equal(3, bind.SetterIndex);
        Assert.True(bind.SetterIsAsynchronous);
    }

    [Fact]
    public void ElementBoolOverload_BindsTheBooleanValueType()
    {
        var bind = Bind("""Html.Input.Bind("checked", "onchange", () => _b, v => _b = v)""");

        Assert.Equal(SpecialType.System_Boolean, bind.ValueType.SpecialType);
    }

    /// <summary>
    /// The space question, pinned rather than reasoned about. Roslyn answers <c>GetSymbolInfo</c> with
    /// the reduced method for the fluent spelling and the unreduced one for the static spelling, while
    /// an operation's argument parameters are always unreduced. All three must name the same argument.
    /// </summary>
    [Theory]
    [InlineData("""Html.Input.Bind("value", "oninput", () => _s, v => _s = v)""")]
    [InlineData("""Decorations.Bind(Html.Input, "value", "oninput", () => _s, v => _s = v)""")]
    public void BothSpellings_AgreeOnTheSetter(string expression)
    {
        var (method, operation) = Resolve(expression);
        Assert.True(KnownSymbols.TryGetBindParameters(method, out var bind));

        Assert.Equal(2, bind.GetterIndex);
        Assert.Equal(3, bind.SetterIndex);
        Assert.True(bind.IsSetter(BoundParameter(operation, "v => _s = v")));
        Assert.False(bind.IsSetter(BoundParameter(operation, "() => _s")));
    }

    [Fact]
    public void ComponentSetter_SitsAfterTheGetter()
    {
        var bind = Bind("Html.Component<Probe>().Bind(c => c.Value, () => _s, v => _s = v)");

        Assert.Equal(1, bind.GetterIndex);
        Assert.Equal(2, bind.SetterIndex);
        Assert.False(bind.SetterIsAsynchronous);
    }

    [Fact]
    public void ComponentGetterOnly_DeclaresNoSetter()
    {
        var bind = Bind("Html.Component<Probe>().Bind(c => c.Value, () => _s)");

        Assert.Equal(-1, bind.SetterIndex);
    }

    [Fact]
    public void ComponentAsynchronousSetter_IsAsynchronous()
    {
        var bind = Bind("Html.Component<Probe>().Bind(c => c.Value, () => _s, SetAsync)");

        Assert.Equal(2, bind.SetterIndex);
        Assert.True(bind.SetterIsAsynchronous);
    }

    /// <summary>
    /// The selector is never the setter, even when its single delegate parameter has the bound value's
    /// type — which is what a component declaring a parameter of its own type produces, and what the
    /// type-comparing derivation this helper replaces answered wrongly.
    /// </summary>
    [Fact]
    public void ComponentSelector_IsNotTheSetter_WhenTheValueTypeIsTheComponent()
    {
        var (method, operation) = Resolve(
            "Html.Component<Probe>().Bind(c => c.Self, () => _p, v => _p = v)");
        Assert.True(KnownSymbols.TryGetBindParameters(method, out var bind));

        Assert.False(bind.IsSetter(BoundParameter(operation, "c => c.Self")));
        Assert.True(bind.IsSetter(BoundParameter(operation, "v => _p = v")));
    }

    [Fact]
    public void IsSetter_AnswersFalseForNull()
    {
        var bind = Bind("""Html.Input.Bind("value", "oninput", () => _s, v => _s = v)""");

        Assert.False(bind.IsSetter(null));
    }

    /// <summary>A zero-argument <c>Action</c> is a delegate whose return type is a non-null
    /// <c>void</c> symbol, so only the <c>ReturnsVoid</c> clause keeps it from being read as the
    /// getter.</summary>
    [Fact]
    public void AZeroArgumentActionAheadOfTheGetter_IsNotTheGetter()
    {
        var bind = Bind(
            """Html.Input.ActionAhead("value", "oninput", () => { }, () => _s, v => _s = v)""",
            methodName: "ActionAhead");

        Assert.Equal(3, bind.GetterIndex);
        Assert.Equal(4, bind.SetterIndex);
        Assert.Equal(SpecialType.System_String, bind.ValueType.SpecialType);
    }

    /// <summary>Position alone does not make a setter: the parameter after the getter must also be a
    /// one-argument delegate over the bound value's type.</summary>
    [Fact]
    public void AParameterAfterTheGetterThatDoesNotTakeTheValue_IsNotResolved()
    {
        var (method, _) = Resolve(
            """Html.Input.MistypedSetter("value", "oninput", () => _s, v => { })""",
            methodName: "MistypedSetter");

        Assert.False(KnownSymbols.TryGetBindParameters(method, out _));
    }
}
