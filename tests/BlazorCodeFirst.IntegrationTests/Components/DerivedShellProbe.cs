using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// A two-level layout hierarchy where both levels declare Chrome. Both are partial, so both get their
// own generated RenderView and the derived chrome wins — the correct outcome. The shape exists so
// ComposeLayoutRenderingTests can pin the inheritance behaviour BCF1001 protects.
public abstract partial class ShellProbeBase : ComposeLayoutBase
{
    protected override View Chrome => Div[Span.Class("base-chrome")["base"], Main[Body]];
}

public partial class DerivedShellProbe : ShellProbeBase
{
    protected override View Chrome => Div[Span.Class("derived-chrome")["derived"], Main[Body]];
}

[Layout(typeof(DerivedShellProbe))]
public partial class DerivedShellProbePage : ComposeComponentBase
{
    protected override View Body => P.Class("derived-page-content")["page"];
}
