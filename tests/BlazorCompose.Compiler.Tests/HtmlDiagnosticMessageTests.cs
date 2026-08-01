namespace BlazorCompose.Compiler.Tests;

public sealed class HtmlDiagnosticMessageTests
{
    [Fact]
    public void BC3003_Message_SuggestsElementWrapNotVStack()
    {
        var d = BlazorCompose.Compiler.Diagnostics.DiagnosticDescriptors.BC3003;
        Assert.DoesNotContain("VStack", d.MessageFormat.ToString());
    }

    [Fact]
    public void BC3003_Description_MentionsFragmentAndRaw()
    {
        var d = BlazorCompose.Compiler.Diagnostics.DiagnosticDescriptors.BC3003;
        var desc = d.Description.ToString();
        Assert.Contains("Fragment", desc, System.StringComparison.Ordinal);
        Assert.Contains("Raw", desc, System.StringComparison.Ordinal);
    }
}
