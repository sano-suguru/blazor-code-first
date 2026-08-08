using BlazorCodeFirst;
using RazorInteropFixture;
using static BlazorCodeFirst.Html;

namespace RazorInterop.Package;

public partial class Host : BodyComponentBase
{
    protected override View Body => Component<ReferencedRazorComponent>();
}
