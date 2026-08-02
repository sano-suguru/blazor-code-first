namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlDiagnosticMessageTests
{
    [Fact]
    public void BCF3003_Message_SuggestsElementWrapNotVStack()
    {
        var d = BlazorCodeFirst.Compiler.Diagnostics.DiagnosticDescriptors.BCF3003;
        Assert.DoesNotContain("VStack", d.MessageFormat.ToString());
    }

    [Fact]
    public void BCF3003_Description_MentionsFragmentAndRaw()
    {
        var d = BlazorCodeFirst.Compiler.Diagnostics.DiagnosticDescriptors.BCF3003;
        var desc = d.Description.ToString();
        Assert.Contains("Fragment", desc, System.StringComparison.Ordinal);
        Assert.Contains("Raw", desc, System.StringComparison.Ordinal);
    }
}
