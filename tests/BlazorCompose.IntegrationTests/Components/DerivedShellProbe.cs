using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

// A two-level layout hierarchy where both levels declare Chrome. Both are partial, so both get their
// own generated RenderView and the derived chrome wins — the correct outcome. The shape exists so
// ComposeLayoutRenderingTests can pin the inheritance behaviour BC1001 protects.
public abstract partial class ShellProbeBase : ComposeLayoutBase
{
    protected override View Chrome => Div(Span("base").Class("base-chrome"), Main(Body));
}

public partial class DerivedShellProbe : ShellProbeBase
{
    protected override View Chrome => Div(Span("derived").Class("derived-chrome"), Main(Body));
}

[Layout(typeof(DerivedShellProbe))]
public partial class DerivedShellProbePage : ComposeComponentBase
{
    protected override View Body => P("page").Class("derived-page-content");
}
