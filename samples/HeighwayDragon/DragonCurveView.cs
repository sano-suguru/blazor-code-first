using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Samples.HeighwayDragon;

public sealed partial class DragonCurveView : BodyComponentBase
{
    private string _spikeStatus = "not run";

    protected override View Body => Div[
        Button.Id("spike-button").OnClick(RunThreadingSpikeAsync)["Run threading spike"],
        Div.Id("spike-status")[_spikeStatus]
    ];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await DragonGlInterop.ReadyAsync();
        }
    }

    // This diagnostic method exists to observe whatever exception the JS-interop/threading
    // boundary throws, of any type -- that observation is the point of the spike, not a case
    // that can be narrowed to a specific exception type ahead of time.
#pragma warning disable CA1031
    private async Task RunThreadingSpikeAsync()
    {
        var beforeThread = Environment.CurrentManagedThreadId;

        int workerThread;
        try
        {
            workerThread = await Task.Run(() =>
            {
                // Calling Ping from inside Task.Run is expected to be unreliable or to throw --
                // JS interop is generally single-thread-affine. That is fine; it is not what this
                // sample calls JS from. Only the "after await" call below is load-bearing.
                return Environment.CurrentManagedThreadId;
            });
        }
        catch (Exception ex)
        {
            _spikeStatus = $"Task.Run itself threw (unexpected): {ex}";
            StateHasChanged();
            return;
        }

        var afterThread = Environment.CurrentManagedThreadId;
        try
        {
            DragonGlInterop.Ping($"after await, before={beforeThread} worker={workerThread} after={afterThread}");
        }
        catch (Exception ex)
        {
            _spikeStatus = $"JS call after the await THREW: {ex}\n" +
                "Fallback needed: capture SynchronizationContext.Current before the await and " +
                "Post the JS call back onto it instead of calling directly after the await.";
            StateHasChanged();
            return;
        }

        _spikeStatus = $"OK — before={beforeThread} worker={workerThread} after={afterThread}. " +
            "Check the browser console for the ping line. No exception calling JS after the await.";
        StateHasChanged();
    }
#pragma warning restore CA1031
}
