using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WasmPackageApp;

/// <summary>
/// The app's one component, and as small as a component can be: what this fixture measures is in
/// the csproj's ItemGroup, not in anything the surface says here. Public and in a namespace because
/// the alternatives both draw an analyzer -- an internal root is CA1812, since the only thing that
/// constructs it is <c>RootComponents.Add&lt;Root&gt;</c>, which the analyzer does not read as a
/// construction, and a public type declared beside the top-level statements would have no
/// namespace, which is CA1050.
/// </summary>
public sealed partial class Root : BodyComponentBase
{
    protected override View Body => Div.Class("root")["ok"];
}
