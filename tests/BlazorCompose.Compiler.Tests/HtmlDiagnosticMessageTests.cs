namespace BlazorCompose.Compiler.Tests;

public sealed class HtmlDiagnosticMessageTests
{
    [Fact]
    public void BC3008_Message_NamesElementsNotOldVocabulary()
    {
        var d = BlazorCompose.Compiler.Diagnostics.DiagnosticDescriptors.BC3008;
        var msg = d.MessageFormat.ToString();
        Assert.DoesNotContain("VStack", msg);
        Assert.Contains("element", msg, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BC3008_Message_NamesFragmentAndRaw()
    {
        var d = BlazorCompose.Compiler.Diagnostics.DiagnosticDescriptors.BC3008;
        var msg = d.MessageFormat.ToString();
        Assert.Contains("Fragment", msg, System.StringComparison.Ordinal);
        Assert.Contains("Raw", msg, System.StringComparison.Ordinal);
    }

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
