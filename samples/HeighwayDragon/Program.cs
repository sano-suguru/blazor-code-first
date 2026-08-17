using BlazorCodeFirst.Samples.HeighwayDragon;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<DragonCurveView>("#app");
await builder.Build().RunAsync();
