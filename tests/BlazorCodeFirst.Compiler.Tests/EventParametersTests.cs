using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds <see cref="KnownSymbols.TryGetEventParameters"/> against the referenced runtime's own event
/// decorations, and against two probe shapes the runtime does not declare.
/// </summary>
/// <remarks>
/// The event counterpart of <see cref="BindParametersTests"/> and measured the same way: against the real
/// surface, with user-declared extension methods in the same compilation standing in for the shapes that
/// would make the three former conventions disagree. The helper answers from shape and never asks whether
/// the method was classified, so handing it a probe is legitimate (#221).
/// </remarks>
public sealed class EventParametersTests
{
    private const string Probes = """
        using BlazorCodeFirst;

        public static class ShapeProbes
        {
            // The hazard: an .On-shaped decoration carrying a second delegate. Both parameters satisfy
            // "is delegate-typed", which is the rule the exemption used before it asked KnownSymbols.
            public static ElementBuilder TwoHandlers(
                this ElementBuilder element, string e,
                System.Action handler, System.Action<int> completed) => element;

            // A delegate written ahead of the event's name rather than after it.
            public static ElementBuilder HandlerFirst(
                this ElementBuilder element, System.Action handler, string e) => element;
        }

        public class Host
        {
            private int _n;

            public View Subject => {{EXPRESSION}};
        }
        """;

    /// <summary>Compiles <paramref name="expression"/> into <see cref="Probes"/> and resolves the named
    /// invocation through <see cref="SurfaceInvocationProbe"/>, which holds the both-spellings mechanics
    /// this and <see cref="BindParametersTests"/> share.</summary>
    private static (IMethodSymbol Method, IInvocationOperation Operation) Resolve(
        string expression, string methodName) =>
        SurfaceInvocationProbe.Resolve(Probes.Replace("{{EXPRESSION}}", expression), methodName);

    private static EventParameters Events(string expression, string methodName)
    {
        var (method, _) = Resolve(expression, methodName);
        Assert.True(KnownSymbols.TryGetEventParameters(method, out var eventParameters));
        return eventParameters;
    }

    private static IParameterSymbol BoundParameter(IInvocationOperation operation, string argumentText) =>
        SurfaceInvocationProbe.BoundParameter(operation, argumentText);

    [Fact]
    public void NamedShortcut_TakesTheHandlerAloneAndNamesTheEventItself()
    {
        var eventParameters = Events("Html.Button.OnClick(() => _n++)", "OnClick");

        Assert.Equal(0, eventParameters.HandlerIndex);
        Assert.Equal(-1, eventParameters.EventNameIndex);
    }

    [Fact]
    public void On_TakesTheEventNameAheadOfTheHandler()
    {
        var eventParameters = Events("""Html.Div.On("onclick", () => _n++)""", "On");

        Assert.Equal(1, eventParameters.HandlerIndex);
        Assert.Equal(0, eventParameters.EventNameIndex);
    }

    /// <summary>
    /// The generic <c>.On&lt;TArgs&gt;</c> overload resolves the same way. Its handler is
    /// <c>Action&lt;TArgs&gt;</c> rather than <c>Action</c>, and a constructed delegate is a delegate, so
    /// the shape rule needs no arity knowledge.
    /// </summary>
    [Fact]
    public void GenericOn_ResolvesItsHandlerLikeTheNonGenericOne()
    {
        var eventParameters = Events(
            """Html.Div.On<Microsoft.AspNetCore.Components.MouseEventArgs>("onclick", a => _n++)""",
            "On");

        Assert.Equal(1, eventParameters.HandlerIndex);
        Assert.Equal(0, eventParameters.EventNameIndex);
    }

    /// <summary>
    /// The space question, pinned rather than reasoned about, exactly as
    /// <c>BindParametersTests.BothSpellings_AgreeOnTheSetter</c> pins it: <c>GetSymbolInfo</c> answers the
    /// reduced method for the fluent spelling and the unreduced one for the static spelling, while an
    /// operation's argument parameters are always unreduced, and all three must name one argument.
    /// </summary>
    [Theory]
    [InlineData("""Html.Div.On("onclick", () => _n++)""")]
    [InlineData("""Decorations.On(Html.Div, "onclick", () => _n++)""")]
    public void BothSpellings_AgreeOnTheHandler(string expression)
    {
        var (method, operation) = Resolve(expression, "On");
        Assert.True(KnownSymbols.TryGetEventParameters(method, out var eventParameters));

        Assert.Equal(1, eventParameters.HandlerIndex);
        Assert.True(eventParameters.IsHandler(BoundParameter(operation, "() => _n++")));
        Assert.False(eventParameters.IsHandler(BoundParameter(operation, "\"onclick\"")));
        // The receiver of the static spelling is an argument too, and argument space has no index for it.
        Assert.False(eventParameters.IsHandler(operation.Arguments[0].Parameter));
    }

    /// <summary>
    /// A named argument moves the handler in the argument list and not in the parameter list, which is why
    /// the role is asked of the parameter an argument bound to rather than of where it was written.
    /// </summary>
    [Fact]
    public void ANamedHandlerArgument_IsStillTheHandler()
    {
        var (method, operation) = Resolve("""Html.Div.On(handler: () => _n++, eventName: "onclick")""", "On");
        Assert.True(KnownSymbols.TryGetEventParameters(method, out var eventParameters));

        Assert.True(eventParameters.IsHandler(BoundParameter(operation, "handler: () => _n++")));
        Assert.False(eventParameters.IsHandler(BoundParameter(operation, "eventName: \"onclick\"")));
    }

    [Fact]
    public void IsHandler_AnswersFalseForNull()
    {
        var eventParameters = Events("""Html.Div.On("onclick", () => _n++)""", "On");

        Assert.False(eventParameters.IsHandler(null));
    }

    /// <summary>
    /// A second delegate parameter is not ranked against the first. There is no rule here that would tell a
    /// handler from an options or completion callback, so the shape has no answer, and answering
    /// <see langword="false"/> is what makes <c>KnownSymbolsSyncTests</c> red the moment such an overload is
    /// declared on the runtime rather than letting one reader exempt the wrong argument.
    /// </summary>
    [Fact]
    public void ASecondDelegateParameter_HasNoAnswer()
    {
        var (method, _) = Resolve(
            """Html.Div.TwoHandlers("onclick", () => _n++, v => _n = v)""", "TwoHandlers");

        Assert.False(KnownSymbols.TryGetEventParameters(method, out _));
    }

    /// <summary>Position is required as well as shape: the handler is the last argument, so a delegate
    /// written ahead of the event's name is not one this compiler was written against.</summary>
    [Fact]
    public void ADelegateAheadOfTheEventName_HasNoAnswer()
    {
        var (method, _) = Resolve("""Html.Div.HandlerFirst(() => _n++, "onclick")""", "HandlerFirst");

        Assert.False(KnownSymbols.TryGetEventParameters(method, out _));
    }

    /// <summary>A decoration carrying no delegate at all is not an event decoration. <c>.Attr</c> is the
    /// nearest neighbour to read wrongly: it shares <c>.On</c>'s arity and its leading name argument, and
    /// differs only in that its second argument carries a value rather than a handler.</summary>
    [Fact]
    public void ADecorationWithNoDelegate_HasNoAnswer()
    {
        var (method, _) = Resolve("""Html.Div.Attr("data-n", "1")""", "Attr");

        Assert.False(KnownSymbols.TryGetEventParameters(method, out _));
    }
}
