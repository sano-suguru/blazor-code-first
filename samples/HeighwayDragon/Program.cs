using System.Runtime.Versioning;
using BlazorCodeFirst.Samples.HeighwayDragon;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// This app only ever runs as browser-wasm; tells the platform-compatibility analyzer so the
// browser-only JSHost/JSImport APIs (DragonGlInterop.cs) don't trip CA1416.
[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<DragonCurveView>("#app");
await builder.Build().RunAsync();
