using BlazorCodeFirst;
using RazorInteropFixture;
using static BlazorCodeFirst.Html;

namespace RazorInterop.ProjectReference;

public partial class Host : BodyComponentBase
{
    protected override View Body => Component<ReferencedRazorComponent>();
}
