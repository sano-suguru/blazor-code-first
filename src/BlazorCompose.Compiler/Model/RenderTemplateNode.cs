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

internal sealed record TextTemplateNode(
    ExpressionTemplate Content,
    EquatableArray<ExpressionTemplate> Classes = default) : RenderTemplateNode;

internal sealed record ButtonTemplateNode(
    ExpressionTemplate Label,
    ExpressionTemplate Handler,
    EquatableArray<ExpressionTemplate> Classes = default) : RenderTemplateNode;

internal sealed record VStackTemplateNode(
    EquatableArray<RenderTemplateNode> Children,
    EquatableArray<ExpressionTemplate> Classes = default) : RenderTemplateNode;

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
