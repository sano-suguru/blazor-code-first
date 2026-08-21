namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The regression guard for "an element tag alias is same-compilation only" (#173), the same shape
/// <see cref="ComponentUnresolvedTypeTests.Component_FromMetadataReference_ReportsNoBCF3012"/> pins for
/// a component type, mirrored onto BCF1006's Error rather than a component's silent success.
/// </summary>
public sealed class HtmlElementTagAliasGeneratorTests
{
    [Fact]
    public void ElementTagAlias_FromMetadataReference_ReportsBCF1006()
    {
        const string library = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace Lib;
            public static class Aliases
            {
                public static ElementView MyCard => Element("my-card");
            }
            """;

        var reference = CompilationTestHost.CompileToMetadataReference(library, "LibAssembly");

        const string host = """
            using BlazorCodeFirst;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Lib.Aliases.MyCard["x"];
            }
            """;

        var compilation = CompilationTestHost.CreateCompilation([("Host.cs", host)], reference);
        var result = CompilationTestHost.RunGenerator(compilation);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1006");
    }
}
