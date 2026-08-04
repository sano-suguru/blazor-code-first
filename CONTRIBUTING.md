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

`BlazorCodeFirst.slnx` contains eight projects:

- `src/BlazorCodeFirst.Runtime`: runtime types (`BodyComponentBase`, the inert
  element helpers, the `ElementBuilder` decorators and child-list indexer that
  `View` results come from, `Component<T>` interop).
- `src/BlazorCodeFirst.Compiler`: the Roslyn source generator and analyzers.
- `tests/BlazorCodeFirst.Runtime.Tests`, `tests/BlazorCodeFirst.Compiler.Tests`,
  `tests/BlazorCodeFirst.IntegrationTests`: unit, generator/analyzer, and
  Blazor-rendering tests.
- `tests/BlazorCodeFirst.DiagnosticTests`: end-to-end diagnostic verification. It
  builds the deliberately broken projects under `tests/diagnostic-fixtures` with
  real MSBuild and asserts on what the compiler actually reported. The other
  diagnostic tests drive the generator in-process and cannot see whether a
  diagnostic reaches a build at all.
- `tests/BlazorCodeFirst.WebAppTestHost`: a Blazor Web App with an
  `InteractiveServer` render mode. The only project here that prerenders over a
  real HTTP pipeline and then hydrates, which no other test layer can observe.
  Run it with `dotnet watch` to look at generated output in a browser.
- `tests/BlazorCodeFirst.WebAppTests`: asserts that host's prerendered HTML
  through `WebApplicationFactory`.

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

# The measurements published in DESIGN.md §7.2 (diff cost and component teardown)
dotnet test tests/BlazorCodeFirst.IntegrationTests/BlazorCodeFirst.IntegrationTests.csproj \
  --filter FullyQualifiedName~DiffCostTests --logger "console;verbosity=detailed"

# The measurements published in DESIGN.md §7.1 (allocations per render)
dotnet run -c Release --project tests/BlazorCodeFirst.Benchmarks -- --filter '*'
```

These deliberately omit `--no-build`, which reuses whatever was compiled last and
so reports a pass for code that was never compiled. CI can pass it
(`ci.yml` builds in the preceding step); a local edit-and-test loop cannot.

Every figure in `DESIGN.md` §7.1 and §7.2 comes from the last two commands. Both
compare against something, and both refuse to report a number unless the two
sides render frame-for-frame equivalent output apart from sequence numbers: the
§7.2 comparison asserts it in `VariantEquivalenceTests`, and the benchmark exits
non-zero from `Program.Main` before BenchmarkDotNet starts. A number from a
mismatched comparison would describe the mismatch, not the compilation strategy.

No CI step runs either one. A published figure has to be reproducible on demand,
which is a lower bar than a per-PR gate; gating would need a noise threshold and
a failure policy that nothing has decided yet. The §7.2 assertions do ride the
ordinary `dotnet test BlazorCodeFirst.slnx` run, because they are tests — that
follows from where they live, and is not a gate on the numbers.

The benchmark project holds a second measurement set that is **not** published.
`StaticFoldBenchmarks` compares folded markup frames against element frames to
decide #140, and it reports times, which §7.1 deliberately does not publish
because the variance is large and machine-dependent. No CI step runs it either,
for the same reason as above. `--filter '*'` picks it up along with the §7.1
benchmarks; to run only it:

```bash
# The #140 decision input only (not a DESIGN.md figure)
dotnet run -c Release --project tests/BlazorCodeFirst.Benchmarks -- --filter '*StaticFoldBenchmarks*'
```

Its two component pairs deliberately render *different* frames, because that
difference is what is being measured, so the frame-equivalence gate cannot cover
them. `Program.Main` gates the inverse condition instead — the folded spelling
must emit strictly fewer frames — and `FoldFixtureTests` asserts each pair
renders the same DOM.

A new diagnostic needs a fixture shape and an entry in
`DiagnosticExpectations.All`; the coverage guard fails until every descriptor is
listed there or excluded with a reason.

Three projects (the TrimTestApp and both `diagnostic-fixtures/*.Package`
fixtures) purge an isolated `blazorcodefirst/0.1.0-dev` NuGet cache before
restoring, so a rebuilt package is never shadowed by a stale one. Get that path
wrong and nothing fails: the tests pass against the old package contents. If you
change it, prove the purge still fires with a direct restore, which prints
`Purging stale dev cache: <path>`:

```bash
dotnet restore tests/diagnostic-fixtures/GeneratorDelivery.Package/GeneratorDelivery.Package.csproj \
  --configfile tests/diagnostic-fixtures/GeneratorDelivery.Package/NuGet.config
```

`dotnet test` will not show it, because MSBuild builds those fixtures inside the
test process and their output never surfaces.

`SnapshotCorpusTests` compares the generator's complete emitted source against
baselines committed under `tests/BlazorCodeFirst.Compiler.Tests/Snapshots`, which
pins sequence numbers and frame order in a way the substring assertions
elsewhere cannot. When a change to the emitter is intended, rewrite them:

```bash
BLAZORCODEFIRST_UPDATE_SNAPSHOTS=1 \
  dotnet test tests/BlazorCodeFirst.Compiler.Tests/BlazorCodeFirst.Compiler.Tests.csproj \
  --filter FullyQualifiedName~SnapshotCorpusTests
```

That still fails the run after writing, by design. Review the resulting diff,
then re-run without the variable, so that changing a baseline stays a deliberate
and reviewed act.

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

Hot Reload against the test host: `dotnet watch --project tests/BlazorCodeFirst.WebAppTestHost/BlazorCodeFirst.WebAppTestHost.csproj`.

To read the `RenderView` the generator actually emitted for a project, which is
the fastest way to confirm what a `Body` lowered to and the only way to see
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

Every issue gets exactly one `area:` label, plus GitHub's default type labels
(`bug`, `enhancement`, `documentation`, `question`, `invalid`) where the type is
unambiguous:

- `area: compiler`: source generator, analyzers, diagnostics.
- `area: surface`: the public authoring API and how UI is written.
- `area: site`: the documentation site and its CI.
- `area: docs`: `README`, `DESIGN`, `ARCHITECTURE`, `CONTRIBUTING` prose.
- `area: build`: the repository's own build, CI, and dependency toolchain.

Two status labels carry process rather than subject:

- `blocked`: has an unresolved prerequisite. Always pair it with a comment
  naming the blocker, so the dependency is readable from the blocked issue
  instead of only from the blocking one.
- `decision`: the deliverable is a decision, not code. Implementation is a
  separate issue that the decision unblocks.

Hierarchy uses sub-issues, not labels. When an issue is genuinely a part of
another rather than merely related, attach it as a sub-issue. A cross-reference
in prose is for "these interact"; a sub-issue is for "this does not ship
independently".

Name milestones for the outcome they deliver rather than numbering them. A
milestone here is a bucket with a progress bar and nothing more, and numbered
names like `M7` or `RM4` drag a per-milestone spec-and-plan ritual back with
them. An issue that is large and undated, or explicitly unscheduled, carries no
milestone at all.

Diagnostic IDs were renamed from `BC****` to `BCF****` in 2026-08 (#103), along
with the package itself. The four digits did not change, so `BC1001` in an older
issue, commit message, or review comment is today's `BCF1001`. The same change
renamed `ComposeComponentBase` to `BodyComponentBase` and `ComposeLayoutBase` to
`ChromeLayoutBase`.

## Conventions the code must uphold

- Sequence numbers are source syntax positions, never runtime generation order.
  Allocate them statically with preorder traversal and give mutually exclusive
  branches disjoint ranges.
- `ForEach` requires a key that represents item identity. Sequence numbers
  identify template positions; keys identify data instances.
- Classes that declare the design-time expression override (`Body` or `Chrome`)
  must be `partial` so the generator can emit `RenderView` (otherwise
  `BCF1001`), and must be top-level classes (nested classes are rejected with
  `BCF1005`). Merely inheriting a BlazorCodeFirst base does not require it. A
  hand-written `RenderView` override suppresses generation entirely, and with it
  every diagnostic about the design-time expression including `BCF1001`:
  nothing is generated into that class, so `partial` would change nothing.
- `Body`, `Chrome`, the element helpers, decorators, `Component<T>()`,
  `Fragment`, and `Raw` are inert design-time constructs. The design-time
  expression (`BodyComponentBase.Body` or `ChromeLayoutBase.Chrome`) must
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
  named (`BCF3012`), because source generators cannot observe each other's
  output.
- `Component<T>()[children]` binds children to `ChildContent`, mirroring Razor's
  rule that nested content becomes `ChildContent`. `BCF3013` and `BCF3014` fence
  off the shapes that cannot work; 付録A states the exact conditions.
- Value expressions copied into generated code must be lexical-context
  independent, because the generated file carries no `using` directives.
  Resolved type names are normalized to `global::`-qualified names and an
  unresolved one reports `BCF3015`. Keep this separate from `BCF3012`, which is
  reserved for the render-node type argument of `Component<T>()`.
- Diagnostic IDs listed in `AnalyzerReleases.Shipped.md` are published
  specification contracts, so do not repurpose or remove them. New IDs and public
  APIs must be tracked in the corresponding `Unshipped` / `PublicAPI` files or
  the analyzer build gates (RS2000/RS0016) fail.
- `ARCHITECTURE.md` 付録A is the canonical diagnostic table, and
  `DiagnosticTableTests` checks it against `DiagnosticDescriptors` in both
  directions: a descriptor with no row fails, and a row with no descriptor fails
  unless the ID is recorded in
  `DiagnosticExpectations.DocumentedWithoutDescriptor` with the reason it is
  specified ahead of its implementation. The 種別 column is checked against
  `DefaultSeverity`, so changing a diagnostic's severity is a change to the table
  as well. The other prose that names a diagnostic, meaning this file,
  `DESIGN.md`, `site/content`, and the public XML docs, cannot be checked
  mechanically, so update it in the same change.
- The `Microsoft.CodeAnalysis.CSharp` version is a compatibility floor imposed on
  consumers of the generator, not a dependency to keep current. Raising it is a
  breaking change for anyone on an older toolset. The rationale and the exact
  floor are recorded on the `PackageVersion` entry in `Directory.Packages.props`.
  The two `Microsoft.CodeAnalysis.*Analyzers` packages carry no such constraint
  and move independently.

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
behavior with real, deterministic collaborators over interaction-based mocks.
Test doubles are reserved for boundaries such as remote services, wall-clock
time, and randomness. Compiler tests are the exception that may reach past
observable behavior into generated source, sequence numbers, incremental cache
behavior, and diagnostic spans, because those are architectural contracts. Test
files may correspond to production types, but a one-to-one file mapping is not
required; group tests by cohesive capability.

Documentation and source comments are written in English.
