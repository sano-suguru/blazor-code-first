using BlazorCodeFirst.WebAppTestHost.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
app.UseAntiforgery();

// Serves the framework's own blazor.web.js. Removing this leaves a host that prerenders correctly
// and then never hydrates, which is the regression PrerenderTests.Counter_page_can_hydrate catches.
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
