# Samples

Standalone applications that consume the packed `BlazorCodeFirst` NuGet package the way a real
project would, unlike `site/`, which references the compiler and runtime projects directly.

`BlazorCodeFirst` is not yet published to nuget.org (issue #295). Until it is, pack it locally before
restoring any sample:

    dotnet pack src/BlazorCodeFirst.Runtime/BlazorCodeFirst.Runtime.csproj -c Release -o artifacts/package

Then, from the repository root:

    dotnet restore samples/Samples.slnx
    dotnet build samples/Samples.slnx --no-restore

## Guestbook

An ASP.NET Core hosted Blazor Web App, unlike `HeighwayDragon`'s standalone WASM SPA: static SSR by
default, one `InteractiveServer` island. A create form goes through `Component<EditForm>()`, each
entry's delete form through the hand-written `.FormName()` element route with a runtime-computed name,
and a live search widget through `.RenderMode(RenderMode.InteractiveServer)`. See
`docs/superpowers/specs/2026-08-20-static-ssr-guestbook-sample-design.md` for the design (issue #485).

Run it with hot reload:

    dotnet watch --project samples/Guestbook/BlazorCodeFirst.Samples.Guestbook.csproj

## HeighwayDragon

A Heighway dragon curve renderer: C# generates up to 16.7M vertices off the UI thread
(`WasmEnableThreads`), uploads them to a WebGL2 buffer, and draws one `LINE_STRIP`. Drag to pan,
scroll to zoom, the slider picks the order. See
`docs/superpowers/specs/2026-08-17-heighway-dragon-sample-design.md` for the design (issue #295).

Run it with hot reload:

    dotnet watch --project samples/HeighwayDragon/BlazorCodeFirst.Samples.HeighwayDragon.csproj
