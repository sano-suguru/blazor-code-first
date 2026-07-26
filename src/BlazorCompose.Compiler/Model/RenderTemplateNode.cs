using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCompose.Compiler;

/// <summary>
/// Symbol-free, value-equal capture of a source location for a template node.  Stores only the
/// primitive coordinates required to reconstruct a <see cref="Location"/> at diagnostic-report time,
/// following the same discipline as <see cref="Diagnostics.DiagnosticInfo"/>.
/// </summary>
internal readonly record struct TemplateLocation(
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan)
{
    public static TemplateLocation From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new TemplateLocation(lineSpan.Path ?? string.Empty, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

internal sealed record ComposableInvocationArgument(
    int ParameterOrdinal,
    int SourceOrder,
    string ParameterTypeName,
    bool IsImplicitDefault,
    ExpressionTemplate Value);

internal abstract record RenderTemplateNode;

internal sealed record IfTemplateNode(
    ExpressionTemplate Condition,
    RenderTemplateNode Then,
    RenderTemplateNode? Otherwise) : RenderTemplateNode;

internal sealed record ComposableCallTemplateNode(
    string MethodKey,
    string DisplayName,
    EquatableArray<ComposableInvocationArgument> Arguments,
    TemplateLocation Location) : RenderTemplateNode;

internal sealed record ForEachTemplateNode(
    ExpressionTemplate Source,
    ExpressionTemplate Key,
    RenderTemplateNode Content,
    TemplateLocation Location) : RenderTemplateNode;

internal sealed record ComponentTemplateNode(
    string TypeName,
    EquatableArray<ComponentParameter> Parameters) : RenderTemplateNode;

internal sealed record EventTemplate(string Name, ExpressionTemplate Handler);

/// <summary>An element attribute: a resolved constant name plus a value expression template.</summary>
internal sealed record AttributeTemplate(string Name, ExpressionTemplate Value);

internal sealed record ElementTemplateNode(
    string Tag,
    EquatableArray<ExpressionTemplate> Classes = default,       // folded class channel (RM1)
    EquatableArray<AttributeTemplate> Attributes = default,     // one frame each (RM2)
    EquatableArray<EventTemplate> Events = default,             // one frame each
    EquatableArray<RenderTemplateNode> Children = default) : RenderTemplateNode;

internal sealed record TextContentTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

/// <summary>Wrapper-less grouping: children emitted in sequence with no enclosing element frame.</summary>
internal sealed record FragmentTemplateNode(
    EquatableArray<RenderTemplateNode> Children = default) : RenderTemplateNode;

/// <summary>Trusted raw HTML injected verbatim via AddMarkupContent (MarkupString-equivalent).</summary>
internal sealed record RawMarkupTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

/// <summary>An externally supplied RenderFragment placed as content via AddContent (no wrapping element).</summary>
internal sealed record RenderFragmentContentTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;
