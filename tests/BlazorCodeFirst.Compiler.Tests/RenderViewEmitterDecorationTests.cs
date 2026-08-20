using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class RenderViewEmitterDecorationTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null, RootNode: root)).ToString();

    private static ElementNode Span(ExpressionTemplate content, EquatableArray<ExpressionTemplate> classes = default) =>
        new("span", classes, default, default, ImmutableArray.Create<RenderNode>(new TextContentNode(content)));

    private static ElementNode Button(
        ExpressionTemplate label,
        ExpressionTemplate handler,
        EquatableArray<ExpressionTemplate> classes = default) =>
        new(
            "button",
            classes,
            default,
            ImmutableArray.Create(new EventTemplate("onclick", handler)),
            ImmutableArray.Create<RenderNode>(new TextContentNode(label)));

    [Fact]
    public void Emit_SingleClassSpan_EmitsClassAttributeVerbatimAtSeqPlusOne()
    {
        var node = Span(
            ExpressionTemplate.Literal("\"Hi\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"badge\"")));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.OpenElement(0, \"span\");", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"badge\");", generated);
        Assert.Contains("__builder.AddContent(2, \"Hi\");", generated);
    }

    [Fact]
    public void Emit_MultipleClassesSpan_FoldsIntoOneParenthesizedJoinCall()
    {
        var node = Span(
            ExpressionTemplate.Literal("\"Hi\""),
            ImmutableArray.Create(
                ExpressionTemplate.Literal("\"a\""),
                ExpressionTemplate.Literal("\"b\"")));

        var generated = EmitRoot(node);

        Assert.Contains(
            "__builder.AddAttribute(1, \"class\", __BlazorCodeFirstJoinClasses((\"a\"), (\"b\")));",
            generated);
        Assert.Contains("__builder.AddContent(2, \"Hi\");", generated);
    }

    [Fact]
    public void Emit_DynamicClassSpan_EmitsExpressionAsAttributeValue()
    {
        var node = Span(
            ExpressionTemplate.Literal("\"Hi\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("_on ? \"on\" : \"off\"")));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"class\", _on ? \"on\" : \"off\");", generated);
    }

    [Fact]
    public void Emit_DecoratedButton_EmitsClassBeforeOnclick()
    {
        var node = Button(
            ExpressionTemplate.Literal("\"OK\""),
            ExpressionTemplate.Literal("OnOk"),
            ImmutableArray.Create(
                ExpressionTemplate.Literal("\"btn\""),
                ExpressionTemplate.Literal("\"primary\"")));

        var generated = EmitRoot(node);

        Assert.Contains(
            "__builder.AddAttribute(1, \"class\", __BlazorCodeFirstJoinClasses((\"btn\"), (\"primary\")));",
            generated);
        Assert.Contains("__builder.AddAttribute(2, \"onclick\", ", generated);
        Assert.Contains("__builder.AddContent(3, \"OK\");", generated);

        int classIdx = generated.IndexOf("\"class\"", StringComparison.Ordinal);
        int onclickIdx = generated.IndexOf("\"onclick\"", StringComparison.Ordinal);
        Assert.True(classIdx >= 0 && onclickIdx >= 0 && classIdx < onclickIdx,
            "class attribute must be emitted before onclick");
    }

    [Fact]
    public void Emit_UndecoratedSpan_EmitsNoClassAttribute()
    {
        var generated = EmitRoot(Span(ExpressionTemplate.Literal("\"Hi\"")));

        Assert.DoesNotContain("\"class\"", generated);
        Assert.Contains("__builder.AddContent(1, \"Hi\");", generated);
    }

    [Fact]
    public void EmitElement_EmitsClassThenAttributesThenEventsThenChildren_InSequenceOrder()
    {
        var node = new ElementNode(
            "a",
            ImmutableArray.Create(ExpressionTemplate.Literal("\"nav\"")),
            ImmutableArray.Create(new AttributeTemplate("href", ExpressionTemplate.Literal("\"/a\""))),
            ImmutableArray.Create(new EventTemplate("onclick", ExpressionTemplate.Literal("() => { }"))),
            ImmutableArray.Create<RenderNode>(new TextContentNode(ExpressionTemplate.Literal("\"Home\""))));

        var code = EmitRoot(node);

        Assert.Contains("__builder.OpenElement(0, \"a\");", code);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"nav\");", code);
        Assert.Contains("__builder.AddAttribute(2, \"href\", \"/a\");", code);
        Assert.Contains(
            "__builder.AddAttribute(3, \"onclick\", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => { }));",
            code);
        Assert.Contains("__builder.AddContent(4, \"Home\");", code);
    }

    [Fact]
    public void EmitElement_EmittedSequenceArguments_AreDense()
    {
        // Regression guard for the emitter's sequence arithmetic: the numbers emitted for a decorated
        // element with attributes, events, and nested children must form a dense run with no gap and no
        // repeat. SequenceArguments.AssertDense records why this replaced a comparison against an
        // independently computed width (#69).
        var node = new ElementNode(
            "a",
            ImmutableArray.Create(ExpressionTemplate.Literal("\"nav\"")),
            ImmutableArray.Create(
                new AttributeTemplate("href", ExpressionTemplate.Literal("\"/a\"")),
                new AttributeTemplate("id", ExpressionTemplate.Literal("\"x\""))),
            ImmutableArray.Create(new EventTemplate("onclick", ExpressionTemplate.Literal("() => { }"))),
            ImmutableArray.Create<RenderNode>(
                new TextContentNode(ExpressionTemplate.Literal("\"Home\"")),
                new ElementNode("span", default, default, default,
                    ImmutableArray.Create<RenderNode>(new TextContentNode(ExpressionTemplate.Literal("\"!\""))))));

        SequenceArguments.AssertDense(EmitRoot(node));
    }

    private static ElementNode BoundInput(BindTemplate bind) =>
        new("input", default, default, default, default) { Bindings = ImmutableArray.Create(bind) };

    [Fact]
    public void Emit_BoundElement_EmitsValueThenBinderThenUpdatesName()
    {
        var node = BoundInput(BindFixtures.Inverted("value", "oninput", "_name"));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.OpenElement(0, \"input\");", generated);
        Assert.Contains("__builder.AddAttribute(1, \"value\", _name);", generated);
        Assert.Contains(
            "__builder.AddAttribute(2, \"oninput\", "
            + BindFixtures.CreateBinder + "__value => _name = __value, _name));",
            generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
        Assert.Contains("__builder.CloseElement();", generated);
    }

    [Fact]
    public void Emit_BoundElementWithSynchronousSetter_CastsTheSetterToAction()
    {
        // The cast names the bound type the template carries, and it is required: a lambda written in an
        // argument position has no natural type, and CreateBinder's own overloads cannot pick one for it
        // once the setter has travelled through a template.
        var node = BoundInput(new BindTemplate(
            "value",
            "oninput",
            ExpressionTemplate.Literal("Query"),
            "global::System.String",
            ExpressionTemplate.Literal("v => Query = v.Trim()"),
            SetterIsAsynchronous: false));

        var generated = EmitRoot(node);

        Assert.Contains(
            "__builder.AddAttribute(2, \"oninput\", " + BindFixtures.CreateBinder
            + "(global::System.Action<global::System.String>)(v => Query = v.Trim()), Query));",
            generated);
    }

    [Fact]
    public void Emit_BoundElementWithAsynchronousSetter_WrapsTheSetterInInferredBindSetter()
    {
        // No cast here: CreateInferredBindSetter infers the delegate type from its own arguments, so the
        // bound type name is not spelled at all on this path.
        var node = BoundInput(new BindTemplate(
            "value",
            "oninput",
            ExpressionTemplate.Literal("_name"),
            "global::System.String",
            ExpressionTemplate.Literal("SetAsync"),
            SetterIsAsynchronous: true));

        var generated = EmitRoot(node);

        Assert.Contains(
            "__builder.AddAttribute(2, \"oninput\", " + BindFixtures.CreateBinder
            + BindFixtures.CreateInferredBindSetter + "callback: SetAsync, value: _name), _name));",
            generated);
        Assert.DoesNotContain("global::System.Action<", generated);
    }

    [Fact]
    public void Emit_BoundToAttributeOtherThanValueOrChecked_EmitsNoUpdatesAttributeName()
    {
        // EventFieldInfo sends back the element's own value (or checked, on a checkbox) and nothing
        // else, and RenderTreeUpdater writes that value into whichever frame this names. On a form
        // element, naming a third attribute writes the input's value into an unrelated frame of the
        // retained tree and strands the real one; on any other element the call is dead, because
        // EventFieldInfo.fromEvent returns null there. Neither is worth emitting.
        var node = BoundInput(BindFixtures.Inverted("data-x", "onfocus", "_x"));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"data-x\", _x);", generated);
        Assert.Contains("__builder.AddAttribute(2, \"onfocus\", " + BindFixtures.CreateBinder, generated);
        Assert.DoesNotContain("SetUpdatesAttributeName", generated);
    }

    [Fact]
    public void Emit_BoundCheckbox_EmitsUpdatesAttributeNameForChecked()
    {
        var node = BoundInput(BindFixtures.Inverted("checked", "onchange", "_agreed"));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.SetUpdatesAttributeName(\"checked\");", generated);
    }

    [Fact]
    public void Emit_BoundElementWithOtherDecorations_NumbersBindFramesAfterThem()
    {
        var node = new ElementNode(
            "input",
            ImmutableArray.Create(ExpressionTemplate.Literal("\"field\"")),
            ImmutableArray.Create(new AttributeTemplate("type", ExpressionTemplate.Literal("\"text\""))),
            default,
            default)
        {
            Bindings = ImmutableArray.Create(BindFixtures.Inverted("value", "oninput", "_name")),
        };

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"class\", \"field\");", generated);
        Assert.Contains("__builder.AddAttribute(2, \"type\", \"text\");", generated);
        Assert.Contains("__builder.AddAttribute(3, \"value\", _name);", generated);
        Assert.Contains("__builder.AddAttribute(4, \"oninput\", " + BindFixtures.CreateBinder, generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
    }

    [Fact]
    public void Emit_UnboundElement_EmitsNoUpdatesAttributeName()
    {
        var generated = EmitRoot(Span(ExpressionTemplate.Literal("\"Hi\"")));

        Assert.DoesNotContain("SetUpdatesAttributeName", generated);
    }

    [Fact]
    public void Emit_TwoBindings_EmitsBothPairsInSourceOrderWithOneUpdatesName()
    {
        var node = new ElementNode("input", default, default, default, default)
        {
            Bindings = ImmutableArray.Create(
                BindFixtures.Inverted("value", "oninput", "_live"),
                BindFixtures.Inverted("data-committed", "onchange", "_committed")),
        };

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"value\", _live);", generated);
        Assert.Contains(
            "__builder.AddAttribute(2, \"oninput\", " + BindFixtures.CreateBinder
            + "__value => _live = __value, _live));",
            generated);
        Assert.Contains("__builder.AddAttribute(3, \"data-committed\", _committed);", generated);
        Assert.Contains(
            "__builder.AddAttribute(4, \"onchange\", " + BindFixtures.CreateBinder
            + "__value => _committed = __value, _committed));",
            generated);

        // A name is recorded for the first binding and none for the second: the emitter records one only
        // when the bound attribute is "value" or "checked", the two the client can send back, and
        // "data-committed" is neither. Nothing here says an element may record at most one name — an
        // element carrying both "value" and "checked" would emit two calls, and the surface declines to
        // diagnose that shape rather than ruling it out.
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
        Assert.DoesNotContain("SetUpdatesAttributeName(\"data-committed\")", generated);
    }

    [Fact]
    public void Emit_TwoBindings_EmittedSequenceArguments_AreDense()
    {
        var node = new ElementNode(
            "input",
            default,
            ImmutableArray.Create(new AttributeTemplate("type", ExpressionTemplate.Literal("\"text\""))),
            default,
            default)
        {
            Bindings = ImmutableArray.Create(
                BindFixtures.Inverted("value", "oninput", "_live"),
                BindFixtures.Inverted("data-committed", "onchange", "_committed")),
        };

        SequenceArguments.AssertDense(EmitRoot(node));
    }
}
