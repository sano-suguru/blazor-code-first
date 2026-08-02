using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ComponentInteropTests
{
    private const string ChildSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Child : ComponentBase
        {
            [Parameter] public string Label { get; set; } = "";
            [Parameter] public string Title { get; set; } = "";
            public string NotAParam { get; set; } = "";
        }
        """;

    [Fact]
    public void Component_ParamSelectsCapturedVariableMember_ReportsBCF3005AndNoSource()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                private static readonly Child _other = new();
                protected override View Body => Component<Child>().Param(c => _other.Label, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3005" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void Component_ParamTargetsNonParameterProperty_ReportsBCF3006AndNoSource()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Child>().Param(c => c.NotAParam, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3006" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void Component_WithParameter_EmitsOpenComponentAndAddComponentParameter()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Child>().Param(c => c.Label, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("OpenComponent<global::T.Child>", generated);
        Assert.Contains("AddComponentParameter(1, \"Label\", \"hi\")", generated);
        Assert.Contains("CloseComponent();", generated);
    }

    [Fact]
    public void ForEach_WithComponentContent_EmitsSetKeyOnComponentAndNoBCF3003()
    {
        const string host = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                private readonly List<Item> _items = new();
                protected override View Body =>
                    ForEach(_items, key: i => i.Id, content: i => Component<Child>().Param(c => c.Label, i.Name));
                public sealed record Item(int Id, string Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3003");
        CompilationTestHost.AssertOutputCompiles(result);

        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        int openIdx = generated.IndexOf("OpenComponent<global::T.Child>", System.StringComparison.Ordinal);
        int keyIdx = generated.IndexOf("SetKey(", System.StringComparison.Ordinal);
        Assert.True(openIdx >= 0, "component should be opened");
        Assert.True(keyIdx > openIdx, "SetKey must be emitted after OpenComponent");
    }

    [Fact]
    public void Component_MultipleParams_EmitsAddComponentParameterInSourceOrder()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Child>().Param(c => c.Label, "hi").Param(c => c.Title, "there");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();

        int firstIdx = generated.IndexOf(
            "AddComponentParameter(1, \"Label\", \"hi\")", System.StringComparison.Ordinal);
        int secondIdx = generated.IndexOf(
            "AddComponentParameter(2, \"Title\", \"there\")", System.StringComparison.Ordinal);
        Assert.True(firstIdx >= 0, "first parameter should be emitted");
        Assert.True(secondIdx > firstIdx, "AddComponentParameter calls must appear in source order");
    }

    [Fact]
    public void Component_DuplicateParamOnSameProperty_ReportsBCF3007AndNoSource()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Child>().Param(c => c.Label, "a").Param(c => c.Label, "b");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3007" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void Component_DistinctParams_DoNotReportBCF3007()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Child>().Param(c => c.Label, "a").Param(c => c.Title, "b");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3007");
    }

    private const string InheritedParamSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class BaseChild : ComponentBase
        {
            [Parameter] public virtual string Value { get; set; } = "";
        }
        public class DerivedChild : BaseChild
        {
            public override string Value { get; set; } = "";
        }
        """;

    [Fact]
    public void Component_ParamTargetsOverriddenInheritedParameter_DoesNotReportBCF3006()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<DerivedChild>().Param(c => c.Value, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Inherited.cs", InheritedParamSource), ("Host.cs", host));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3006");
        CompilationTestHost.AssertOutputCompiles(result);
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("AddComponentParameter(1, \"Value\", \"hi\")", generated);
    }

    [Fact]
    public void Component_ParamTargetsMultiLevelOverriddenParameter_DoesNotReportBCF3006()
    {
        const string chain = """
            using Microsoft.AspNetCore.Components;
            namespace T;
            public class A : ComponentBase { [Parameter] public virtual string Value { get; set; } = ""; }
            public class B : A { public override string Value { get; set; } = ""; }
            public class C : B { public override string Value { get; set; } = ""; }
            """;
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<C>().Param(c => c.Value, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Chain.cs", chain), ("Host.cs", host));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3006");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Component_ParamTargetsNewShadowedNonParameter_ReportsBCF3006()
    {
        const string shadow = """
            using Microsoft.AspNetCore.Components;
            namespace T;
            public class ShadowBase : ComponentBase { [Parameter] public string Value { get; set; } = ""; }
            public class ShadowDerived : ShadowBase { public new string Value { get; set; } = ""; }
            """;
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<ShadowDerived>().Param(c => c.Value, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Shadow.cs", shadow), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3006" && d.Severity == DiagnosticSeverity.Error);
    }
}
