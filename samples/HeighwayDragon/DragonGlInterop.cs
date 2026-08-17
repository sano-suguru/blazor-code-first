using System.Runtime.InteropServices.JavaScript;

namespace BlazorCodeFirst.Samples.HeighwayDragon;

internal static partial class DragonGlInterop
{
    private const string ModuleName = "dragon-gl";
    private const string ModulePath = "/dragon-gl.js";

    public static Task ReadyAsync() => JSHost.ImportAsync(ModuleName, ModulePath);

    [JSImport("ping", ModuleName)]
    public static partial void Ping(string tag);
}
