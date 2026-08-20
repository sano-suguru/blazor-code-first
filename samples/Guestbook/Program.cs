using BlazorCodeFirst.Samples.Guestbook;
using BlazorCodeFirst.Samples.Guestbook.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GuestbookStore>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
