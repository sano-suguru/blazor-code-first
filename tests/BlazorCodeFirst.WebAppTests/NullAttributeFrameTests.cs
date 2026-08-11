using BlazorCodeFirst.WebAppTestHost.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.WebAppTests;

/// <summary>
/// The premise <c>null-attribute.spec.ts</c> rests on: a null attribute value appends no frame at all, so
/// what the browser is asked about is a genuinely absent attribute. If that ever changed — a frame appended
/// carrying a null value, say — the browser assertions could keep passing while measuring something else,
/// which is the failure mode recorded at the end of <c>ARCHITECTURE.md</c> §2.7(D) and the reason
/// <c>FoldParityTests</c> exists in this same shape. This is the .NET-side half of that gate, and
/// <c>dotnet test</c> is what runs it.
/// </summary>
public sealed class NullAttributeFrameTests
{
    /// <summary>The attribute frames a build of <paramref name="view"/> appends.</summary>
    private static int AttributeFrameCount(NullAttributeView view)
    {
        var builder = new RenderTreeBuilder();
        view.Build(builder);

        var frames = builder.GetFrames();
        var count = 0;
        for (var index = 0; index < frames.Count; index++)
        {
            if (frames.Array[index].FrameType == RenderTreeFrameType.Attribute)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Four of the view's decorations stop appending a frame in the absent state: the null <c>title</c>,
    /// the lone null <c>class</c>, the <see langword="false"/> <c>data-v</c>, and the join whose every
    /// term turns null. The two joins that keep a non-null term do <em>not</em> — their null term is
    /// dropped from the join (#236), so what reaches <c>AddAttribute</c> is a shorter string and not
    /// none. That split is the whole of #236, and the count is where it is pinned: a fifth would mean a
    /// join had started omitting an attribute that still has a class to carry.
    /// </summary>
    [Fact]
    public void NullAttributeView_InTheAbsentState_AppendsFourFewerAttributeFrames()
    {
        var view = new NullAttributeView();
        var present = AttributeFrameCount(view);

        view.Toggle();

        Assert.Equal(present - 4, AttributeFrameCount(view));
    }

    /// <summary>
    /// The absolute count in the present state, so the relative assertion above cannot be satisfied by both
    /// sides shrinking together. Seventeen: eight <c>id</c>s on the spans and one on the button, the
    /// button's <c>onclick</c>, the two <c>title</c>s, the four <c>class</c>es, and <c>data-v</c>.
    /// </summary>
    [Fact]
    public void NullAttributeView_InThePresentState_AppendsSeventeenAttributeFrames()
    {
        Assert.Equal(17, AttributeFrameCount(new NullAttributeView()));
    }
}
