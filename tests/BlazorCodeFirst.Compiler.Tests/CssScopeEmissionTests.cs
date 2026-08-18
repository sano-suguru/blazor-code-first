using Xunit;

namespace BlazorCodeFirst.Compiler.Tests;

public class CssScopeEmissionTests
{
    private const string Source = """
        using BlazorCodeFirst;

        public partial class Counter : BodyComponentBase
        {
            protected override View Body => Html.Div.OnClick(() => { });
        }
        """;

    [Fact]
    public void ScopedElement_EmitsBareScopeAttributeAfterBindingsBeforeChildren()
    {
        var result = CompilationTestHost.RunGeneratorWithCssScopes(
            [("Counter.cs", Source)], [("Counter.cs.css", "bcf-abcd1234")]);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // OpenElement(0) is the div; AddAttribute(1) is the click handler (an Attribute frame);
        // AddAttribute(2) must be the bare scope attribute, immediately after it and before
        // CloseElement.
        Assert.Contains("__builder.AddAttribute(2, \"bcf-abcd1234\");", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void UnscopedElement_EmitsNoScopeAttribute()
    {
        var result = CompilationTestHost.RunGeneratorWithCssScopes([("Counter.cs", Source)], []);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("bcf-", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}
