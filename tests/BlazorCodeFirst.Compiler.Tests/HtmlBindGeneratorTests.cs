namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlBindGeneratorTests
{
    /// <summary>
    /// Runs the generator over <paramref name="body"/> placed in a component, and returns the generated
    /// text. The output is compiled, not merely inspected: the binder is a call the generated file has no
    /// <c>using</c> for, so a spelling that reads correctly can still fail to bind.
    /// </summary>
    private static string GenerateBody(string body)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        Assert.DoesNotContain(
            result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    [Fact]
    public void Bind_StringGetterOnly_InvertsGetterIntoSetter()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Type("text").Bind("value", "oninput", () => _name);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__builder.AddAttribute(2, \"value\", _name);", generated);
        Assert.Contains(
            "__builder.AddAttribute(3, \"oninput\", "
            + BindFixtures.CreateBinder + "__value => _name = __value, _name));",
            generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
    }

    [Fact]
    public void Bind_PrivateSetterOnTheComponentItself_InvertsGetterIntoSetter()
    {
        // The half of BCF3018's accessibility check that must not fire. RenderView is emitted into a
        // partial of this same class, so a setter private to it is reachable and the binding stands —
        // which is why the check asks the compilation about the component type rather than asking whether
        // the setter is public (#391). GenerateBody compiles the output, so a wrong answer here is a
        // CS0272 and not a silently accepted string.
        const string body = """
            public string Name { get; private set; } = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => Name);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__value => Name = __value, Name)", generated);
    }

    [Fact]
    public void Bind_BoolGetterOnly_InvertsGetterIntoSetter()
    {
        const string body = """
            private bool _agreed;
            protected override View Body =>
                Html.Input.Type("checkbox").Bind("checked", "onchange", () => _agreed);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__builder.AddAttribute(2, \"checked\", _agreed);", generated);
        Assert.Contains("__value => _agreed = __value, _agreed)", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"checked\");", generated);
    }

    [Fact]
    public void Bind_MemberChainGetter_InvertsWholeChain()
    {
        const string body = """
            private sealed class FormModel { public string Name { get; set; } = ""; }
            private readonly FormModel _form = new();
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _form.Name);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__value => _form.Name = __value", generated);
    }

    [Fact]
    public void Bind_ExplicitSyncSetter_TransplantsAuthorLambdaCastToAction()
    {
        const string body = """
            private string Query { get; set; } = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => Query, v => Query = v.Trim());
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            BindFixtures.CreateBinder + "(global::System.Action<global::System.String>)(v => Query = v.Trim()), Query)",
            generated);
    }

    [Fact]
    public void Bind_InsideViewPart_SubstitutesTheValueOnBothSidesOfTheBinding()
    {
        // Closes a gap Task 3's review found: ViewPartExpander substitutes parameter holes into
        // BindTemplate.Value, and nothing exercises that branch. The emitter tests build ElementNode
        // directly and never reach the expander. If the .Substitute call is dropped, ToCode() throws
        // "Expression template still contains unbound parameter holes" and only a test shaped like this
        // one sees it. The setter channel has its own case below.
        //
        // The hole is a *member* of the view part's parameter, not the parameter itself. Expansion
        // replaces the parameter with a generated local holding a copy of the caller's argument, so an
        // inverted setter written over the parameter alone would assign to that copy and the caller's
        // field would never see it; BCF3018 rejects that shape, and this one writes through the copied
        // reference to the object the caller passed.
        const string body = """
            private sealed class FormModel { public string Name { get; set; } = ""; }
            private readonly FormModel _form = new();

            [ViewPart]
            private static View Field(FormModel model) =>
                Html.Input.Bind("value", "oninput", () => model.Name);

            protected override View Body => Html.Div[Field(_form)];
            """;

        var generated = GenerateBody(body);

        // The parameter hole is filled with the expansion local on both sides of the binding, and the
        // same local names both, which is what proves neither .Substitute was dropped.
        Assert.Contains("__builder.AddAttribute(2, \"value\", __bcf_arg_1_0.Name);", generated);
        Assert.Contains(
            "__value => __bcf_arg_1_0.Name = __value, __bcf_arg_1_0.Name)", generated);
    }

    [Fact]
    public void Bind_ExplicitSetterInsideViewPart_SubstitutesTheSetterToo()
    {
        // The setter is the binding's second expression channel and its own .Substitute call in the
        // expander. The inverted case above cannot see it: with no setter written there is nothing on
        // that channel to leave unsubstituted, and ToCode() would go on to throw only for the value.
        const string body = """
            private sealed class FormModel { public string Name { get; set; } = ""; }
            private readonly FormModel _form = new();

            [ViewPart]
            private static View Field(FormModel model) =>
                Html.Input.Bind("value", "oninput", () => model.Name, v => model.Name = v.Trim());

            protected override View Body => Html.Div[Field(_form)];
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            BindFixtures.CreateBinder
            + "(global::System.Action<global::System.String>)"
            + "(v => __bcf_arg_1_0.Name = v.Trim()), __bcf_arg_1_0.Name)",
            generated);
    }

    [Fact]
    public void Bind_ExplicitAsyncSetter_WrapsInInferredBindSetter()
    {
        const string body = """
            private string _name = "";
            private System.Threading.Tasks.Task SetAsync(string v)
            {
                _name = v;
                return System.Threading.Tasks.Task.CompletedTask;
            }
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name, SetAsync);
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            BindFixtures.CreateInferredBindSetter + "callback: SetAsync, value: _name)",
            generated);
    }

    [Fact]
    public void Bind_StringGetterOnly_CarriesNoNullableMismatchDiagnostic()
    {
        // Regression: the framework's own CreateBinder(receiver, Action<string?> setter, string
        // existingValue, ...) overload annotates its setter nullable, so `__value => _name = __value`
        // triggers CS8601 wherever the assignment target's own declaration is nullable-enabled — hence
        // the explicit #nullable enable on the user source below, which GenerateBody's helper omits and
        // every other test in this file relies on that omission to stay silent on this exact defect.
        // AssertOutputCompiles alone would not have caught it either way: CS8601 is a warning, not an
        // error, until a project (any real one; this repository's own Directory.Build.props included)
        // escalates warnings to errors. Task 9's BindRenderingTests found this by building for real.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Type("text").Bind("value", "oninput", () => _name);
            """;

        var source = $$"""
            #nullable enable
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            d => d.Id is "CS8601" or "CS8620");
    }

    [Fact]
    public void Bind_ExplicitSyncSetter_CarriesNoNullableMismatchDiagnostic()
    {
        // Same regression as the getter-only case, on the (Action<string>)(<setter>) cast: the cast's
        // own type does not match CreateBinder's Action<string?> parameter, which is CS8620 rather than
        // CS8601 but the same underlying mismatch. Unlike CS8601 above, this one does not depend on the
        // user source's own nullable context — the mismatch is entirely between two casts inside the
        // generated file itself — but #nullable enable is kept here too so both tests model the same
        // (realistic, nullable-enabled) consuming project.
        const string body = """
            private string Query { get; set; } = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => Query, v => Query = v.Trim());
            """;

        var source = $$"""
            #nullable enable
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            d => d.Id is "CS8601" or "CS8620");
    }

    [Fact]
    public void Bind_OnAnElementWhoseOtherChannelsAreAllConstant_StillEmitsFrames()
    {
        // Every other channel here is a compile-time constant and 'input' is a foldable void tag, so
        // without the binding this whole element serializes to one AddMarkupContent frame. Markup can
        // express neither the binder nor SetUpdatesAttributeName, so a fold would leave a plain
        // value="" attribute behind and drop the binding, with nothing failing to compile to say so.
        //
        // Written against the frames rather than against the predicate that produces them: the element
        // path is the outcome that has to survive, whichever part of the fold decides it.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Class("field").Type("text").Bind("value", "oninput", () => _name);
            """;

        var generated = GenerateBody(body);

        Assert.DoesNotContain("AddMarkupContent", generated);
        Assert.Contains("__builder.OpenElement(0, \"input\");", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"field\");", generated);
        Assert.Contains("__builder.AddAttribute(2, \"type\", \"text\");", generated);
        Assert.Contains("__builder.AddAttribute(3, \"value\", _name);", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
        Assert.Contains("__builder.CloseElement();", generated);
    }

    [Fact]
    public void TwoBindings_OnOneElement_CompileAndEmitBothPairs()
    {
        const string body = """
            private string _live = "";
            private string _committed = "";
            protected override View Body =>
                Html.Input
                    .Bind("value", "oninput", () => _live)
                    .Bind("data-committed", "onchange", () => _committed);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__builder.AddAttribute(1, \"value\", _live);", generated);
        Assert.Contains("__builder.AddAttribute(3, \"data-committed\", _committed);", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
        Assert.DoesNotContain("SetUpdatesAttributeName(\"data-committed\")", generated);
    }

    [Fact]
    public void TwoBinds_InsideViewPart_SubstituteEveryBindingsHoles()
    {
        // The expander maps over every binding on the element. Reduced to substituting Bindings[0], the
        // second binding would vanish from the expansion with nothing failing — the same class of break
        // the fold predicate guards against, reached through the expander instead: a binding silently
        // dropped, leaving a plain attribute behind. The single-binding case above cannot see it, since
        // there is no second element of the collection to lose.
        const string body = """
            private sealed class FormModel
            {
                public string Live { get; set; } = "";
                public string Committed { get; set; } = "";
            }
            private readonly FormModel _form = new();

            [ViewPart]
            private static View Field(FormModel model) =>
                Html.Input
                    .Bind("value", "oninput", () => model.Live)
                    .Bind("data-committed", "onchange", () => model.Committed);

            protected override View Body => Html.Div[Field(_form)];
            """;

        var generated = GenerateBody(body);

        // Both bindings carry the expansion local on both sides, which is what proves the loop ran to the
        // end rather than substituting the first binding and copying the rest through unsubstituted.
        Assert.Contains("__builder.AddAttribute(2, \"value\", __bcf_arg_1_0.Live);", generated);
        Assert.Contains("__value => __bcf_arg_1_0.Live = __value, __bcf_arg_1_0.Live)", generated);
        Assert.Contains("__builder.AddAttribute(4, \"data-committed\", __bcf_arg_1_0.Committed);", generated);
        Assert.Contains("__value => __bcf_arg_1_0.Committed = __value, __bcf_arg_1_0.Committed)", generated);
    }

    /// <summary>
    /// A binding written with a culture formats through the framework's converter, under the culture the
    /// call site wrote. That is the whole of what #158 said a non-string value could not have, and the
    /// reason #307 could open this surface.
    /// </summary>
    [Fact]
    public void Bind_IntWithCulture_FormatsThroughBindConverterAndPassesTheCulture()
    {
        const string body = """
            private int _age;
            protected override View Body =>
                Html.Input.Type("number").Bind(
                    "value", "oninput", () => _age,
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            "__builder.AddAttribute(2, \"value\", "
            + "global::Microsoft.AspNetCore.Components.BindConverter.FormatValue(_age, "
            + "culture: global::System.Globalization.CultureInfo.InvariantCulture));",
            generated);
        Assert.Contains(
            "__builder.AddAttribute(3, \"oninput\", "
            + BindFixtures.CreateBinder + "__value => _age = __value, _age, "
            + "culture: global::System.Globalization.CultureInfo.InvariantCulture));",
            generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
    }

    [Fact]
    public void Bind_DateWithFormatAndCulture_PassesBothToBothCalls()
    {
        const string body = """
            private System.DateTime _due;
            protected override View Body =>
                Html.Input.Type("date").Bind(
                    "value", "oninput", () => _due, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.BindConverter.FormatValue(_due, "
            + "format: \"yyyy-MM-dd\", "
            + "culture: global::System.Globalization.CultureInfo.InvariantCulture)",
            generated);
        Assert.Contains(
            "__value => _due = __value, _due, format: \"yyyy-MM-dd\", "
            + "culture: global::System.Globalization.CultureInfo.InvariantCulture)",
            generated);
    }

    /// <summary>
    /// The binder's third argument is the raw value, not the formatted string the attribute frame got.
    /// The two were the same string before #307, which is why <c>Binder</c> used to be handed the
    /// attribute's.
    /// </summary>
    [Fact]
    public void Bind_DecimalWithExplicitSetterAndCulture_PassesTheRawValueToTheBinder()
    {
        const string body = """
            private decimal _amount;
            private void SetAmount(decimal v) => _amount = v;
            protected override View Body =>
                Html.Input.Bind(
                    "value", "oninput", () => _amount, SetAmount,
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            BindFixtures.CreateBinder
            + "(global::System.Action<global::System.Decimal>)(SetAmount), _amount, "
            + "culture: global::System.Globalization.CultureInfo.InvariantCulture));",
            generated);
    }

    [Fact]
    public void Bind_EnumWithCulture_FormatsThroughBindConverter()
    {
        const string body = """
            private System.DayOfWeek _day;
            protected override View Body =>
                Html.Select.Bind(
                    "value", "onchange", () => _day,
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.BindConverter.FormatValue(_day, "
            + "culture: global::System.Globalization.CultureInfo.InvariantCulture)",
            generated);
    }

    /// <summary>
    /// The wrapping follows the resolved overload and never the bound type, so every spelling that
    /// predates #307 emits what it always did.
    /// </summary>
    [Fact]
    public void Bind_StringWithoutCulture_DoesNotWrapInBindConverter()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Type("text").Bind("value", "oninput", () => _name);
            """;

        var generated = GenerateBody(body);

        Assert.DoesNotContain("BindConverter.FormatValue", generated);
        Assert.Contains("__builder.AddAttribute(2, \"value\", _name);", generated);
    }

    /// <summary>
    /// The culture and the format are transplanted expressions like the getter and the setter, so a
    /// <c>[ViewPart]</c> parameter reaches them too. <c>ViewPartExpander</c> substituted the value and the
    /// setter by name and would have carried these two through unsubstituted.
    /// </summary>
    [Fact]
    public void Bind_CultureInsideViewPart_SubstitutesTheCulture()
    {
        const string body = """
            private sealed class FormModel { public int Age { get; set; } }
            private readonly FormModel _form = new();

            [ViewPart]
            private static View Field(FormModel model, System.Globalization.CultureInfo culture) =>
                Html.Input.Bind("value", "oninput", () => model.Age, culture);

            protected override View Body =>
                Html.Div[Field(_form, System.Globalization.CultureInfo.InvariantCulture)];
            """;

        var generated = GenerateBody(body);

        // Both calls carry the expansion local, which is what proves the culture channel is substituted
        // rather than merely carried through: an unsubstituted hole makes ToCode() throw.
        Assert.Contains("culture: __bcf_arg_1_1)", generated);
        Assert.DoesNotContain("culture: culture", generated);
    }
}
