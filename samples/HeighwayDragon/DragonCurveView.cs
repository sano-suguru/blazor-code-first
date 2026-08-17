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

    private ElementReference _canvas;
    private int _order = DefaultOrder;
    private bool _glReady;
    private bool _webglUnavailable;
    private bool _generating;
    private bool _justSucceeded;
    private long _vertexCount;
    private double _workerMs;
    private double _uploadMs;
    private bool _dragging;
    private double _lastPointerX;
    private double _lastPointerY;

    protected override View Body => Div[
        Canvas.Id("gl-canvas").Class("gl-canvas")
            .Ref(r => _canvas = r)
            .On<PointerEventArgs>("onpointerdown", OnPointerDown)
            .On<PointerEventArgs>("onpointermove", OnPointerMove)
            .On<PointerEventArgs>("onpointerup", OnPointerUp)
            .On<WheelEventArgs>("onwheel", OnWheel).PreventDefault(),

        Div.Id("panel").Class("panel")[
            Div.Id("panel-title").Class("panel-title")["HEIGHWAY DRAGON"],

            Div.Id("order-row").Class("panel-row")[
                Div.Id("order-label").Class("panel-label")["ORDER"],
                Div.Id("order-value").Class("panel-value")[_order.ToString(CultureInfo.InvariantCulture)]
            ],
            Input.Type("range").Id("order-slider").Class("order-slider")
                .Attr("min", MinOrder.ToString(CultureInfo.InvariantCulture))
                .Attr("max", MaxOrder.ToString(CultureInfo.InvariantCulture))
                .Attr("disabled", _generating)
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

            Div.Id("status").Class(StatusClass)[StatusText]
        ]
    ];

    private string StatusText =>
        _webglUnavailable ? "WEBGL2 UNAVAILABLE" :
        _generating ? "GENERATING…" :
        _justSucceeded ? "OK" :
        "READY";

    private string StatusClass =>
        _webglUnavailable ? "status status--error" :
        _generating ? "status status--loading" :
        _justSucceeded ? "status status--success" :
        "status";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await DragonGlInterop.ReadyAsync();
        _glReady = await DragonGlInterop.InitAsync(JS, _canvas);
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
        if (!_glReady || _generating ||
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return;
        }

        _order = Math.Clamp(order, MinOrder, MaxOrder);
        await RegenerateAsync(_order);
    }

    private async Task RegenerateAsync(int order)
    {
        _generating = true;
        _justSucceeded = false;
        StateHasChanged();

        var stopwatch = Stopwatch.StartNew();
        var vertexCount = DragonCurveGenerator.VertexCount(order);
        var points = new Point[vertexCount];
        await Task.Run(() => DragonCurveGenerator.FillPoints(points, order));
        _workerMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        var (minX, maxX, minY, maxY) = Bounds(points);
        var pointBytes = MemoryMarshal.AsBytes<Point>(points.AsSpan());
        DragonGlInterop.UploadPoints(pointBytes, vertexCount, minX, maxX, minY, maxY);
        _uploadMs = stopwatch.Elapsed.TotalMilliseconds;

        _vertexCount = vertexCount;

        _generating = false;
        _justSucceeded = true;
        StateHasChanged();
    }

    private static (double minX, double maxX, double minY, double maxY) Bounds(Point[] points)
    {
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;

        foreach (var point in points)
        {
            if (point.X < minX) minX = point.X;
            if (point.X > maxX) maxX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.Y > maxY) maxY = point.Y;
        }

        return (minX, maxX, minY, maxY);
    }

    private void OnPointerDown(PointerEventArgs e)
    {
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
        DragonGlInterop.ZoomBy(e.DeltaY < 0 ? 1.15 : 1.0 / 1.15);
    }
}
