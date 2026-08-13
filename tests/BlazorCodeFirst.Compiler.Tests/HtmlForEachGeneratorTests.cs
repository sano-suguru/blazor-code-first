namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlForEachGeneratorTests
{
    /// <summary>
    /// A component whose <c>Body</c> is one <c>ForEach</c> with the given <paramref name="key"/> and
    /// <paramref name="content"/> arguments, and which is otherwise identical however it is called.
    /// </summary>
    /// <remarks>
    /// Most of the cases below are read in pairs that must differ in the key alone — a keyed and a
    /// declined spelling of one body — and the tests say so. Built from one scaffold, that property is
    /// structural: it cannot be broken by editing one of the two blocks, because there are no longer two
    /// blocks to drift apart.
    /// </remarks>
    private static string Source(string key, string content) => $$"""
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div[Html.ForEach(_items, {{key}}, {{content}})];
        }
        """;

    private const string DeclinedKey = "key: null";
    private const string IdentityKey = "key: x => x";

    private const string ElementContent = "content: x => Html.Span[x]";
    private const string BareTextContent = "content: x => x";
    private const string ConstantContent = """content: x => Html.Span[Html.Em["fixed"]]""";
    private const string FragmentContent = "content: x => Html.Fragment(Html.Span[x], Html.Em[x])";

    /// <summary>A key that is null at runtime but is not the written null the opt-out is spelled as.</summary>
    /// <remarks>
    /// Stands apart from <see cref="Source"/> because it needs a member and a using the scaffold does not
    /// carry, which is the difference under test rather than an accident of spelling.
    /// </remarks>
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
        var result = CompilationTestHost.RunGenerator(Source(IdentityKey, ElementContent));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("foreach (var", generated);
        Assert.Contains("__builder.SetKey(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void ForEach_WithBareTextContent_ReportsBCF3003()
    {
        var result = CompilationTestHost.RunGenerator(Source(IdentityKey, BareTextContent));
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void ForEach_NullKey_EmitsForeachWithoutSetKey()
    {
        // #172: the key is opted out at the call site. The loop is unchanged apart from SetKey, which is
        // what makes the projection form sugar over this one rather than a second mechanism.
        var result = CompilationTestHost.RunGenerator(Source(DeclinedKey, ElementContent));
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
        // (CONTRIBUTING.md §Conventions the code must uphold). The two sides share one scaffold and one
        // content string, so "the key is the only difference" is structural rather than asserted.
        var unkeyed = CompilationTestHost.RunGenerator(Source(DeclinedKey, ConstantContent));
        var keyed = CompilationTestHost.RunGenerator(Source(IdentityKey, ConstantContent));

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
        // ForEach_WithBareTextContent_ReportsBCF3003 above, which runs the same content through the same
        // scaffold with the other key and is the other half of the pair.
        var result = CompilationTestHost.RunGenerator(Source(DeclinedKey, BareTextContent));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEach_NullKeyWithFragmentContent_DoesNotReportBCF3003()
    {
        // The wrapper-less root the Issue names: a Fragment opens no element frame of its own, and with
        // no key that costs nothing.
        var result = CompilationTestHost.RunGenerator(Source(DeclinedKey, FragmentContent));

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
