using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// Exercises the typed .On overload end to end: the handler reads ChangeEventArgs.Value out of the
// event, which no argument-less handler can do.
public partial class TypedEventComponent : BodyComponentBase
{
    private string _name = "";

    protected override View Body =>
        Div[
            Input.Type("text").Attr("value", _name)
                 .On("oninput", (ChangeEventArgs e) => _name = e.Value?.ToString() ?? ""),
            Span[$"Hello, {_name}"]];
}
