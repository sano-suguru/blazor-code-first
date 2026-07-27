using System.Collections.Immutable;
using BlazorCompose.Compiler;

namespace BlazorCompose.Compiler.Tests;

public sealed class RenderViewEmitterComponentTests
{
    [Fact]
    public void Emit_ComponentWithParameters_EmitsOpenComponentAddParametersAndClose()
    {
        var node = new ComponentNode(
            TypeName: "global::MyApp.Counter",
            Parameters: ImmutableArray.Create(
                new ComponentParameter("Start", ExpressionTemplate.Literal("5")),
                new ComponentParameter("Label", ExpressionTemplate.Literal("\"hi\""))));

        var model = new ComponentModel("T.g.cs", "T", default, null, node);

        var generated = RenderViewEmitter.Emit(model).ToString();

        Assert.Contains("__builder.OpenComponent<global::MyApp.Counter>(0);", generated);
        Assert.Contains("__builder.AddComponentParameter(1, \"Start\", 5);", generated);
        Assert.Contains("__builder.AddComponentParameter(2, \"Label\", \"hi\");", generated);
        Assert.Contains("__builder.CloseComponent();", generated);
    }

    [Fact]
    public void Emit_ComponentWithNoParameters_EmitsOpenAndCloseOnly()
    {
        var node = new ComponentNode("global::MyApp.Widget", ImmutableArray<ComponentParameter>.Empty);
        var model = new ComponentModel("T.g.cs", "T", default, null, node);

        var generated = RenderViewEmitter.Emit(model).ToString();

        Assert.Contains("__builder.OpenComponent<global::MyApp.Widget>(0);", generated);
        Assert.Contains("__builder.CloseComponent();", generated);
        Assert.DoesNotContain("AddComponentParameter", generated);
    }
}
