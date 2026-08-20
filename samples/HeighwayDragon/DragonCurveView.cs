using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Samples.HeighwayDragon;

public sealed partial class DragonCurveView : BodyComponentBase
{
    private const int MinOrder = 1;
    private const int MaxOrder = 24;
    private const int DefaultOrder = 14;

    [Inject]
    public required IJSRuntime JS { get; set; }

    private enum GenerationState { Ready, Generating, Succeeded, Failed }

    private ElementReference _canvas;
    private int _order = DefaultOrder;
    private bool _glReady;
    private bool _webglUnavailable;
    private GenerationState _generationState;
    private long _vertexCount;
    private double _workerMs;
    private double _uploadMs;
    private bool _dragging;
    private double _lastPointerX;
    private double _lastPointerY;
    private Point[]? _pointBuffer;

    protected override View Body => Div[
        Canvas.Id("gl-canvas").Class("gl-canvas")
            .Ref(r => _canvas = r)
            .On<PointerEventArgs>("onpointerdown", OnPointerDown)
            .On<PointerEventArgs>("onpointermove", OnPointerMove)
            .On<PointerEventArgs>("onpointerup", OnPointerUp)
            .On<PointerEventArgs>("onpointercancel", OnPointerUp)
            .On<WheelEventArgs>("onwheel", OnWheel).PreventDefault(),

        Div.Id("panel").Class("panel")[
            Div.Id("panel-title").Class("panel-title")["HEIGHWAY DRAGON"],

            Div.Id("order-row").Class("panel-row")[
                Div.Id("order-label").Class("panel-label")["ORDER"],
                Div.Id("order-value").Class("panel-value")[_order.ToString(CultureInfo.InvariantCulture)]
            ],
            Input.Type("range").Id("order-slider").Class("order-slider")
                .Min(MinOrder.ToString(CultureInfo.InvariantCulture))
                .Max(MaxOrder.ToString(CultureInfo.InvariantCulture))
                .Disabled(_generationState == GenerationState.Generating)
                .Bind("value", "oninput",
                    () => _order.ToString(CultureInfo.InvariantCulture),
                    OnOrderInputAsync),

            Div.Id("vertices-row").Class("panel-row")[
                Div.Id("vertices-label").Class("panel-label")["VERTICES"],
                Div.Id("vertices-value").Class("panel-value panel-value--mono")[
                    _vertexCount.ToString("N0", CultureInfo.InvariantCulture)]
            ],
            Div.Id("worker-row").Class("panel-row")[
                Div.Id("worker-label").Class("panel-label")["WORKER"],
                Div.Id("worker-value").Class("panel-value panel-value--mono")[$"{_workerMs:F1} ms"]
            ],
            Div.Id("upload-row").Class("panel-row")[
                Div.Id("upload-label").Class("panel-label")["UPLOAD"],
                Div.Id("upload-value").Class("panel-value panel-value--mono")[$"{_uploadMs:F1} ms"]
            ],

            Div.Id("status").Class(Status.Class)[Status.Text]
        ]
    ];

    private (string Text, string Class) Status =>
        _webglUnavailable ? ("WEBGL2 UNAVAILABLE", "status status--error") :
        _generationState switch
        {
            GenerationState.Generating => ("GENERATING…", "status status--loading"),
            GenerationState.Failed => ("GENERATION FAILED", "status status--error"),
            GenerationState.Succeeded => ("OK", "status status--success"),
            _ => ("READY", "status"),
        };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var readyTask = DragonGlInterop.ReadyAsync();
        var initTask = DragonGlInterop.InitAsync(JS, _canvas);
        await Task.WhenAll(readyTask, initTask);
        _glReady = await initTask;
        if (!_glReady)
        {
            _webglUnavailable = true;
            StateHasChanged();
            return;
        }

        await RegenerateAsync(_order);
    }

    private async Task OnOrderInputAsync(string value)
    {
        if (!_glReady || _generationState == GenerationState.Generating ||
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return;
        }

        _order = Math.Clamp(order, MinOrder, MaxOrder);
        await RegenerateAsync(_order);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Generation/upload can fail in ways specific to this sample's scale (OOM at " +
            "high order, a JS interop JSException, a WebGL error) that aren't worth enumerating. Any " +
            "escaping exception here would otherwise leave _generating stuck true and the UI wedged on " +
            "\"GENERATING…\" with no recovery short of a page reload, so it is surfaced via status " +
            "instead of swallowed or left to escape.")]
    private async Task RegenerateAsync(int order)
    {
        _generationState = GenerationState.Generating;
        StateHasChanged();

        try
        {
            var vertexCount = DragonCurveGenerator.VertexCount(order);
            var stopwatch = Stopwatch.StartNew();
            var bounds = await Task.Run(() =>
            {
                if (_pointBuffer is null || _pointBuffer.Length < vertexCount)
                {
                    _pointBuffer = null;
                    _pointBuffer = GC.AllocateUninitializedArray<Point>(vertexCount);
                }

                return DragonCurveGenerator.FillPoints(_pointBuffer.AsSpan(0, vertexCount), order);
            });
            _workerMs = stopwatch.Elapsed.TotalMilliseconds;

            stopwatch.Restart();
            var (minX, maxX, minY, maxY) = bounds;
            var pointBytes = MemoryMarshal.AsBytes<Point>(_pointBuffer.AsSpan(0, vertexCount));
            DragonGlInterop.UploadPoints(pointBytes, vertexCount, minX, maxX, minY, maxY);
            _uploadMs = stopwatch.Elapsed.TotalMilliseconds;

            _vertexCount = vertexCount;
            _generationState = GenerationState.Succeeded;
        }
        catch (Exception)
        {
            _generationState = GenerationState.Failed;
        }
        finally
        {
            StateHasChanged();
        }
    }

    private void OnPointerDown(PointerEventArgs e)
    {
        if (!_glReady)
        {
            return;
        }

        _dragging = true;
        _lastPointerX = e.ClientX;
        _lastPointerY = e.ClientY;
        DragonGlInterop.CapturePointer(e.PointerId);
    }

    private void OnPointerMove(PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var dx = e.ClientX - _lastPointerX;
        var dy = e.ClientY - _lastPointerY;
        _lastPointerX = e.ClientX;
        _lastPointerY = e.ClientY;

        DragonGlInterop.Pan(dx, dy);
    }

    private void OnPointerUp(PointerEventArgs e)
    {
        _dragging = false;
    }

    private void OnWheel(WheelEventArgs e)
    {
        if (!_glReady)
        {
            return;
        }

        DragonGlInterop.ZoomBy(e.DeltaY < 0 ? 1.15 : 1.0 / 1.15);
    }
}
