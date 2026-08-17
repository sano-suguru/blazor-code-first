using System.Runtime.InteropServices.JavaScript;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorCodeFirst.Samples.HeighwayDragon;

internal static partial class DragonGlInterop
{
    private const string ModuleName = "dragon-gl";
    private const string ModulePath = "/dragon-gl.js";

    public static Task ReadyAsync() => JSHost.ImportAsync(ModuleName, ModulePath);

    /// <summary>
    /// Handles the canvas over classic JSInterop, which marshals <see cref="ElementReference"/>
    /// directly. This targets the same module URL the JSImport methods below load via
    /// <see cref="ReadyAsync"/> -- the browser dedupes ES module instances by resolved URL, so both
    /// interop mechanisms share the same module-scoped `gl`/`canvas` state.
    /// </summary>
    public static async Task<bool> InitAsync(IJSRuntime js, ElementReference canvas)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        return await module.InvokeAsync<bool>("initGl", canvas);
    }

    /// <summary>
    /// <paramref name="pointBytes"/> is the <c>Point[]</c> array reinterpreted as bytes
    /// (<c>MemoryMarshal.AsBytes</c>) -- the source-generated JSImport marshaller only supports
    /// <see cref="JSType.MemoryView"/> over <see cref="byte"/> spans, not <c>float</c> spans
    /// directly (SYSLIB1072). <c>dragon-gl.js</c>'s <c>uploadPoints</c> reinterprets the bytes back
    /// into a <c>Float32Array</c> view over the same buffer, so this still costs no extra copy
    /// beyond the one <see cref="JSType.MemoryView"/> requires either way.
    /// </summary>
    [JSImport("uploadPoints", ModuleName)]
    public static partial void UploadPoints(
        [JSMarshalAs<JSType.MemoryView>] Span<byte> pointBytes,
        int vertexCount, double minX, double maxX, double minY, double maxY);

    [JSImport("pan", ModuleName)]
    public static partial void Pan(double dxPixels, double dyPixels);

    [JSImport("zoomBy", ModuleName)]
    public static partial void ZoomBy(double factor);

    /// <summary>
    /// <see cref="JSType.Number"/>: pointer IDs are always small non-negative integers in
    /// practice, so the double-precision range JS numbers carry loses nothing here.
    /// </summary>
    [JSImport("capturePointer", ModuleName)]
    public static partial void CapturePointer([JSMarshalAs<JSType.Number>] long pointerId);
}
