using System.Linq;

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
    public void BC3003_Message_SuggestsElementWrapNotVStack()
    {
        var d = BlazorCompose.Compiler.Diagnostics.DiagnosticDescriptors.BC3003;
        Assert.DoesNotContain("VStack", d.MessageFormat.ToString());
    }
}
