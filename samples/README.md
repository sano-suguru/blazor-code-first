# Samples

Standalone applications that consume the packed `BlazorCodeFirst` NuGet package the way a real
project would, unlike `site/`, which references the compiler and runtime projects directly.

`BlazorCodeFirst` is not yet published to nuget.org (issue #295). Until it is, pack it locally before
restoring any sample:

    dotnet pack src/BlazorCodeFirst.Runtime/BlazorCodeFirst.Runtime.csproj -c Release -o artifacts/package

Then, from the repository root:

    dotnet restore samples/Samples.slnx
    dotnet build samples/Samples.slnx --no-restore

## HeighwayDragon

A Heighway dragon curve renderer: C# generates up to 16.7M vertices off the UI thread
(`WasmEnableThreads`), uploads them to a WebGL2 buffer, and draws one `LINE_STRIP`. Drag to pan,
scroll to zoom, the slider picks the order. See
`docs/superpowers/specs/2026-08-17-heighway-dragon-sample-design.md` for the design (issue #295).

Run it with hot reload:

    dotnet watch --project samples/HeighwayDragon/BlazorCodeFirst.Samples.HeighwayDragon.csproj
