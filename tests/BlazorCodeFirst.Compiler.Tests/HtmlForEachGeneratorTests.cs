namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlForEachGeneratorTests
{
    private const string ElementContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, x => x, x => Html.Span[x])];
        }
        """;

    private const string TextContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, x => x, x => x)];
        }
        """;

    private const string NullKeySource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, key: null, content: x => Html.Span[x])];
        }
        """;

    /// <summary>
    /// A constant content root, for the fold pair below. Written twice, once with the key and once
    /// without, so the key is the only difference between the two sides.
    /// </summary>
    private const string NullKeyConstantContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, key: null, content: x => Html.Span[Html.Em["fixed"]])];
        }
        """;

    private const string KeyedConstantContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, key: x => x, content: x => Html.Span[Html.Em["fixed"]])];
        }
        """;

    /// <summary>
    /// <see cref="TextContentSource"/> with the key declined, and nothing else. Written as a pair so the
    /// key is the only difference between the two, which is what makes the key the subject under test.
    /// </summary>
    private const string NullKeyTextContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, key: null, content: x => x)];
        }
        """;

    private const string NullKeyFragmentContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, key: null, content: x => Html.Fragment(Html.Span[x], Html.Em[x]))];
        }
        """;

    /// <summary>A key that is null at runtime but is not the written null the opt-out is spelled as.</summary>
    private const string NullValuedKeyVariableSource = """
        using System;
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private static readonly Func<string, object?>? NoKey = null;
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, NoKey, x => Html.Span[x])];
        }
        """;

    [Fact]
    public void ForEach_WithElementContent_EmitsSetKeyOnElement()
    {
        var result = CompilationTestHost.RunGenerator(ElementContentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("foreach (var", generated);
        Assert.Contains("__builder.SetKey(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void ForEach_WithBareTextContent_ReportsBCF3003()
    {
        var result = CompilationTestHost.RunGenerator(TextContentSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void ForEach_NullKey_EmitsForeachWithoutSetKey()
    {
        // #172: the key is opted out at the call site. The loop is unchanged apart from SetKey, which is
        // what makes the projection form sugar over this one rather than a second mechanism.
        var result = CompilationTestHost.RunGenerator(NullKeySource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("foreach (var", generated);
        Assert.DoesNotContain("__builder.SetKey(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3002");
    }

    [Fact]
    public void ForEach_NullKeyWithConstantContent_FoldsWhereTheKeyedSpellingCannot()
    {
        // A threaded key is what keeps a ForEach content *root* out of the fold: EmitNode declines to
        // fold whenever one arrives, and there is no second predicate. With no key the ordinary rule
        // applies and the whole root collapses to one markup frame (ARCHITECTURE.md §2.7 D).
        //
        // Asserted on the root's own markup rather than on the presence of a markup frame, because the
        // key never reached the root's children: the keyed side folds `<em>fixed</em>` inside the span it
        // opens, so both sides carry a markup frame and only the span tells them apart. And asserted as a
        // pair, because a one-sided fold assertion keeps passing when folding stops altogether
        // (CONTRIBUTING.md §Conventions the code must uphold).
        var unkeyed = CompilationTestHost.RunGenerator(NullKeyConstantContentSource);
        var keyed = CompilationTestHost.RunGenerator(KeyedConstantContentSource);

        var unkeyedSource = Assert.Single(unkeyed.GeneratedSources).SourceText.ToString();
        var keyedSource = Assert.Single(keyed.GeneratedSources).SourceText.ToString();

        Assert.Contains("\"<span><em>fixed</em></span>\"", unkeyedSource);
        Assert.DoesNotContain("\"span\"", unkeyedSource);
        Assert.Contains("\"span\"", keyedSource);
        Assert.DoesNotContain("\"<span><em>fixed</em></span>\"", keyedSource);
        CompilationTestHost.AssertOutputCompiles(unkeyed);
        CompilationTestHost.AssertOutputCompiles(keyed);
    }

    [Fact]
    public void ForEach_NullKeyWithBareTextContent_DoesNotReportBCF3003()
    {
        // BCF3003 says "the key cannot attach here". With no key there is nothing to attach, so a root
        // that opens no keyable frame is not a defect (#172). The keyed spelling of this same body is
        // ForEach_WithBareTextContent_ReportsBCF3003 above, which is the other half of the pair.
        var result = CompilationTestHost.RunGenerator(NullKeyTextContentSource);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEach_NullKeyWithFragmentContent_DoesNotReportBCF3003()
    {
        // The wrapper-less root the Issue names: a Fragment opens no element frame of its own, and with
        // no key that costs nothing.
        var result = CompilationTestHost.RunGenerator(NullKeyFragmentContentSource);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEach_KeyHeldInANullValuedVariable_ReportsBCF3004()
    {
        // Absence is read syntactically, as If reads its own otherwise. The generator transplants a body
        // and has no runtime value to test, so a variable that happens to hold null is not the opt-out
        // spelling and stays where every other unreadable key sits.
        var result = CompilationTestHost.RunGenerator(NullValuedKeyVariableSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3004");
    }
}
