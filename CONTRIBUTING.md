# Contributing

Notes for building, testing, and extending BlazorCodeFirst. `DESIGN.md` (product
and design overview) and `ARCHITECTURE.md` (compilation algorithm, sequence
assignment, memory layout) are the authoritative specifications; changes must
stay consistent with both where their decisions overlap.

## Prerequisites

The SDK is pinned in `global.json` to `10.0.300` with `latestPatch`
roll-forward. Repository-wide build settings live in `Directory.Build.props`,
`Directory.Packages.props` (central package management), and `.editorconfig`.

## Solution layout

`BlazorCodeFirst.slnx` contains seven projects:

- `src/BlazorCodeFirst.Runtime` — runtime types (`ComposeComponentBase`, the
  inert element helpers, the `ElementBuilder` decorators and child-list
  indexer that `View` results come from, `Component<T>` interop).
- `src/BlazorCodeFirst.Compiler` — the Roslyn source generator and analyzers.
- `tests/BlazorCodeFirst.Runtime.Tests`, `tests/BlazorCodeFirst.Compiler.Tests`,
  `tests/BlazorCodeFirst.IntegrationTests` — unit, generator/analyzer, and
  Blazor-rendering tests.
- `tests/BlazorCodeFirst.DiagnosticTests` — end-to-end diagnostic verification: it
  builds the deliberately broken projects under `tests/diagnostic-fixtures` with
  real MSBuild and asserts on what the compiler actually reported. The other
  diagnostic tests drive the generator in-process and cannot see whether a
  diagnostic reaches a build at all.
- `samples/BlazorCodeFirst.Samples.Counter` — a runnable sample.

`tests/diagnostic-fixtures` is deliberately outside the solution: every project
there fails to compile by design. See its README before adding one.

`tests/BlazorCodeFirst.TrimTests` and `tests/BlazorCodeFirst.TrimTestApp` live in the
repository but stay outside the solution until the package-based trimming
workflow lands.

## Build and test

```bash
# Restore / build
dotnet restore BlazorCodeFirst.slnx
dotnet build BlazorCodeFirst.slnx --no-restore

# Test everything
dotnet test BlazorCodeFirst.slnx

# One project
dotnet test tests/BlazorCodeFirst.Compiler.Tests/BlazorCodeFirst.Compiler.Tests.csproj

# One case
dotnet test tests/BlazorCodeFirst.Compiler.Tests/BlazorCodeFirst.Compiler.Tests.csproj \
  --filter FullyQualifiedName~GeneratorTests

# Diagnostics as a real build reports them (packs the runtime and builds four fixtures)
dotnet test tests/BlazorCodeFirst.DiagnosticTests/BlazorCodeFirst.DiagnosticTests.csproj
```

These deliberately omit `--no-build`, which reuses whatever was compiled last and
so reports a pass for code that was never compiled. CI can pass it
(`ci.yml` builds in the preceding step); a local edit-and-test loop cannot.

A new diagnostic needs a fixture shape and an entry in
`DiagnosticExpectations.All`; the coverage guard fails until every descriptor is
listed there or excluded with a reason.

`SnapshotCorpusTests` compares the generator's complete emitted source against
baselines committed under `tests/BlazorCodeFirst.Compiler.Tests/Snapshots`, which
pins sequence numbers and frame order in a way the substring assertions
elsewhere cannot. When a change to the emitter is intended, rewrite them:

```bash
BLAZORCODEFIRST_UPDATE_SNAPSHOTS=1 \
  dotnet test tests/BlazorCodeFirst.Compiler.Tests/BlazorCodeFirst.Compiler.Tests.csproj \
  --filter FullyQualifiedName~SnapshotCorpusTests
```

That still fails the run after writing, by design — review the resulting diff,
then re-run without the variable. A baseline is only meaningful if changing it
is a deliberate, reviewed act.

Packaging and trimming:

```bash
# Pack the runtime and verify its layout
dotnet pack src/BlazorCodeFirst.Runtime/BlazorCodeFirst.Runtime.csproj -c Release -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCodeFirst.0.1.0-dev.nupkg

# Publish trimmed and run the trim tests (osx-arm64 shown; linux-x64 also supported)
dotnet publish tests/BlazorCodeFirst.TrimTestApp/BlazorCodeFirst.TrimTestApp.csproj \
  -c Release -r osx-arm64 --self-contained true \
  --configfile tests/BlazorCodeFirst.TrimTestApp/NuGet.config
BLAZORCODEFIRST_TRIM_OUTPUT=$(pwd)/tests/BlazorCodeFirst.TrimTestApp/bin/Release/net10.0/osx-arm64/publish \
  dotnet test tests/BlazorCodeFirst.TrimTests/BlazorCodeFirst.TrimTests.csproj
```

Hot Reload against the sample: `dotnet watch --project samples/BlazorCodeFirst.Samples.Counter/BlazorCodeFirst.Samples.Counter.csproj`.

To read the `RenderView` the generator actually emitted for a project — the
fastest way to confirm what a `Body` lowered to, and the only way to see
emitted output a diagnostic does not describe:

```bash
dotnet build <project> -t:Rebuild \
  -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=gen
```

## Code style

CI runs `dotnet format --verify-no-changes`, which is stricter than the build's
`EnforceCodeStyleInBuild` and fails on any drift. Run it before pushing:

```bash
dotnet format BlazorCodeFirst.slnx --verify-no-changes --no-restore   # check
dotnet format BlazorCodeFirst.slnx                                    # auto-fix
```

Enable the shared pre-push hook once per clone so this runs automatically:
`git config core.hooksPath eng/hooks`.

## Issue tracker

Issues carry the current state and the plan. `DESIGN.md` and `ARCHITECTURE.md`
describe the intended finished design and deliberately do not track progress,
so an Issue is the only place a gap, a defect, or a deferred decision is
recorded.

**Every issue gets exactly one `area:` label**, plus GitHub's default type
labels (`bug`, `enhancement`, `documentation`, `question`, `invalid`) where the
type is unambiguous:

- `area: compiler` — source generator, analyzers, diagnostics.
- `area: surface` — the public authoring API and how UI is written.
- `area: site` — the documentation site and its CI.
- `area: docs` — `README`, `DESIGN`, `ARCHITECTURE`, `CONTRIBUTING` prose.

Two status labels carry process rather than subject:

- `blocked` — has an unresolved prerequisite. **Always pair it with a comment
  naming the blocker.** The label alone tells a reader to go hunting; the point
  is that the dependency is readable from the blocked issue, not only from the
  blocking one.
- `decision` — the deliverable is a decision, not code. Implementation is a
  separate issue that the decision unblocks.

**Hierarchy uses sub-issues, not labels.** When an issue is genuinely a part of
another rather than merely related, attach it as a sub-issue. A cross-reference
in prose is for "these interact"; a sub-issue is for "this does not ship
independently".

**Milestones are named for the outcome they deliver, never numbered.** A
milestone here is a bucket with a progress bar and nothing more. An earlier
`M0`–`M6` chain was folded deliberately: that shape worked while every
milestone was one step in a single backward chain toward publishing the docs
site, and reusing it for gap-closing work turned acceptance criteria into
prose. Numbered names drag the per-milestone spec-and-plan ritual back with
them, so avoid `M7` / `RM4` and describe the outcome instead.

An issue that is large and undated, or explicitly unscheduled, carries no
milestone. That is a valid state rather than an oversight.

## Conventions the code must uphold

- **Sequence numbers are source syntax positions, never runtime generation
  order.** Allocate them statically with preorder traversal and give mutually
  exclusive branches disjoint ranges.
- **`ForEach` requires a key that represents item identity.** Sequence numbers
  identify template positions; keys identify data instances.
- Classes that **declare the design-time expression override** (`Body` or
  `Chrome`) must be `partial` so the generator can emit `RenderView`
  (otherwise `BCF1001`), and must be top-level classes (nested classes are
  rejected with `BCF1005`). Merely inheriting a Compose base does not require
  it. A hand-written `RenderView` override suppresses generation entirely, and
  with it every diagnostic about the design-time expression including `BCF1001`:
  nothing is generated into that class, so `partial` would change nothing.
- `Body`, `Chrome`, the element helpers, decorators, `Component<T>()`,
  `Fragment`, and `Raw` are inert design-time constructs. The design-time
  expression (`ComposeComponentBase.Body` or `ComposeLayoutBase.Chrome`) must
  not be evaluated at runtime or mutate state; state mutation inside it is
  reported as `BCF3001`.
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
  named (`BCF3012`) — source generators cannot observe each other's output. The
  same component in a referenced project or package resolves normally.
  `Component<T>()[children]` binds children to `ChildContent`, mirroring Razor's
  rule that nested content becomes `ChildContent`. The target must have a
  settable `[Parameter] ChildContent` of type non-generic `RenderFragment`;
  otherwise BCF3013 is reported. A `RenderFragment<TContext>` cannot receive the
  children — the generated lambda is non-generic and would fail an invalid cast
  at runtime. Other `RenderFragment` parameters bind through
  `.Param(c => c.Name, content)`. BCF3014 prevents design-time inert types
  (`View` / `ComponentView<T>` / `ElementBuilder`) from being passed to the
  generic `.Param`.
- Value expressions copied into generated code must be lexical-context independent.
  Resolved type names are normalized to `global::`-qualified names. An unresolved
  type name that is not already rooted at `global::` reports `BCF3015`; each generic
  type argument is judged independently. Keep this separate from `BCF3012`, which
  is reserved for the render-node type argument of `Component<T>()`.
- Diagnostic IDs listed in `AnalyzerReleases.Shipped.md` are published
  specification contracts — do not repurpose or remove them. New IDs and public
  APIs must be tracked in the corresponding `Unshipped` / `PublicAPI` files or
  the analyzer build gates (RS2000/RS0016) fail.
- `ARCHITECTURE.md` 付録A is the canonical diagnostic table, and
  `DiagnosticTableTests` checks it against `DiagnosticDescriptors` in both
  directions: a descriptor with no row fails, and a row with no descriptor fails
  unless the ID is recorded in
  `DiagnosticExpectations.DocumentedWithoutDescriptor` with the reason it is
  specified ahead of its implementation. The 種別 column is checked against
  `DefaultSeverity`, so changing a diagnostic's severity is a change to the table
  as well. The other prose that states a
  diagnostic's scope — this file, `DESIGN.md`, and the public XML docs — cannot
  be checked mechanically, so update it in the same change.

## Engineering standard

Treat this as a reference-quality modern .NET codebase, not a minimal proof of
concept. Nullable reference types, deterministic builds, and current
.NET/Roslyn analyzers are enabled; repository-owned code is kept warning-clean.
Prefer modern C# features where they improve clarity, safety, or allocation
behavior without compromising the net10.0 baseline. net11.0-only code is
isolated behind `#if NET11_0_OR_GREATER` with matching tests.

Test behavior at the appropriate layer: runtime unit tests, generator/analyzer
tests that inspect generated source and diagnostics, integration tests against
Blazor rendering, and benchmarks only for performance claims. Test methods are
named `SubjectOrMethod_Scenario_ExpectedBehavior`, and they prefer observable
behavior with real, deterministic collaborators over interaction-based mocks —
test doubles are reserved for boundaries such as remote services, wall-clock
time, and randomness. Compiler tests are the exception that may reach past
observable behavior into generated source, sequence numbers, incremental cache
behavior, and diagnostic spans, because those are architectural contracts. Test
files may correspond to production types, but a one-to-one file mapping is not
required; group tests by cohesive capability.

Documentation and source comments are written in English.
