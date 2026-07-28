# Contributing

Notes for building, testing, and extending BlazorCompose. `DESIGN.md` (product
and design overview) and `ARCHITECTURE.md` (compilation algorithm, sequence
assignment, memory layout) are the authoritative specifications; changes must
stay consistent with both where their decisions overlap.

## Prerequisites

The SDK is pinned in `global.json` to `10.0.300` with `latestPatch`
roll-forward. Repository-wide build settings live in `Directory.Build.props`,
`Directory.Packages.props` (central package management), and `.editorconfig`.

## Solution layout

`BlazorCompose.slnx` contains six projects:

- `src/BlazorCompose.Runtime` — runtime types (`ComposeComponentBase`, the
  inert `View` factories and decorators, `Component<T>` interop).
- `src/BlazorCompose.Compiler` — the Roslyn source generator and analyzers.
- `tests/BlazorCompose.Runtime.Tests`, `tests/BlazorCompose.Compiler.Tests`,
  `tests/BlazorCompose.IntegrationTests` — unit, generator/analyzer, and
  Blazor-rendering tests.
- `samples/BlazorCompose.Samples.Counter` — a runnable sample.

`tests/BlazorCompose.TrimTests` and `tests/BlazorCompose.TrimTestApp` live in the
repository but stay outside the solution until the package-based trimming
workflow lands.

## Build and test

```bash
# Restore / build
dotnet restore BlazorCompose.slnx
dotnet build BlazorCompose.slnx --no-restore

# Test everything
dotnet test BlazorCompose.slnx --no-build

# One project
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --no-build

# One case
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj \
  --no-build --filter FullyQualifiedName~GeneratorTests
```

Packaging and trimming:

```bash
# Pack the runtime and verify its layout
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg

# Publish trimmed and run the trim tests (osx-arm64 shown; linux-x64 also supported)
dotnet publish tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj \
  -c Release -r osx-arm64 --self-contained true \
  --configfile tests/BlazorCompose.TrimTestApp/NuGet.config
BLAZORCOMPOSE_TRIM_OUTPUT=$(pwd)/tests/BlazorCompose.TrimTestApp/bin/Release/net10.0/osx-arm64/publish \
  dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj
```

Hot Reload against the sample: `dotnet watch --project samples/BlazorCompose.Samples.Counter/BlazorCompose.Samples.Counter.csproj`.

## Code style

CI runs `dotnet format --verify-no-changes`, which is stricter than the build's
`EnforceCodeStyleInBuild` and fails on any drift. Run it before pushing:

```bash
dotnet format BlazorCompose.slnx --verify-no-changes --no-restore   # check
dotnet format BlazorCompose.slnx                                    # auto-fix
```

Enable the shared pre-push hook once per clone so this runs automatically:
`git config core.hooksPath eng/hooks`.

## Conventions the code must uphold

- **Sequence numbers are source syntax positions, never runtime generation
  order.** Allocate them statically with preorder traversal and give mutually
  exclusive branches disjoint ranges.
- **`ForEach` requires a key that represents item identity.** Sequence numbers
  identify template positions; keys identify data instances.
- Classes that **declare the design-time expression override** (`Body` or
  `Chrome`) must be `partial` so the generator can emit `RenderView`
  (otherwise `BC1001`), and must be top-level classes (nested classes are
  rejected with `BC1005`). Merely inheriting a Compose base does not require
  it. A hand-written `RenderView` override suppresses generation entirely
  (no generator diagnostic about the design-time expression is reported,
  though `BC1001`'s partial requirement still applies to the class itself).
- `Body`, `Chrome`, factory APIs, and decorators are inert design-time
  constructs. The design-time expression (`ComposeComponentBase.Body` or
  `ComposeLayoutBase.Chrome`) must not be evaluated at runtime or mutate
  state; state mutation inside it is reported as `BC3001`.
- Preserve one-way flow: event dispatch precedes state mutation, which precedes
  rendering and DOM diff application.
- Keep the SSC path free of runtime UI trees, reflection, and runtime
  expression compilation. `Component<T>().Param(...)` must compile to static
  parameter setters and stay trimming/AOT safe.
- Decorator chains collapse into the owning element's emitted attributes rather
  than introducing wrapper nodes or extra frame widths.
- Preserve bidirectional Razor compatibility: generate `...AsFragment` siblings
  for `[Composable]` methods, and support existing Razor components through
  `Component<T>()`. A `Component<T>()` type argument must resolve while the
  generator runs, so a `.razor` component declared in the same project cannot be
  named (`BC3012`) — source generators cannot observe each other's output. The
  same component in a referenced project or package resolves normally.
  `Component<T>(children)` binds children to `ChildContent`, mirroring Razor's
  rule that nested content becomes `ChildContent`. The target must have a
  settable `[Parameter] ChildContent` of type non-generic `RenderFragment`;
  otherwise BC3013 is reported. A `RenderFragment<TContext>` cannot receive the
  children — the generated lambda is non-generic and would fail an invalid cast
  at runtime. Other `RenderFragment` parameters bind through
  `.Param(c => c.Name, content)`. BC3014 prevents design-time inert types
  (`View` / `ComponentView<T>`) from being passed to the generic `.Param`.
- Diagnostic IDs listed in `AnalyzerReleases.Shipped.md` are published
  specification contracts — do not repurpose or remove them. New IDs and public
  APIs must be tracked in the corresponding `Unshipped` / `PublicAPI` files or
  the analyzer build gates (RS2000/RS0016) fail.

## Engineering standard

Treat this as a reference-quality modern .NET codebase, not a minimal proof of
concept. Nullable reference types, deterministic builds, and current
.NET/Roslyn analyzers are enabled; repository-owned code is kept warning-clean.
Prefer modern C# features where they improve clarity, safety, or allocation
behavior without compromising the net10.0 baseline. net11.0-only code is
isolated behind `#if NET11_0_OR_GREATER` with matching tests.

Test behavior at the appropriate layer: runtime unit tests, generator/analyzer
tests that inspect generated source and diagnostics, integration tests against
Blazor rendering, and benchmarks only for performance claims. Documentation and
source comments are written in English.
