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
  element helpers, the `ElementView` decorators and child-list indexer that
  produce `View` results, `Component<T>` interop).
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
  `InteractiveServer` render mode. It is the only project here that prerenders
  over a real HTTP pipeline and then hydrates, which no other test layer can
  observe. Run it with `dotnet watch` to look at generated output in a browser.
- `tests/BlazorCodeFirst.WebAppTests`: asserts that host's prerendered HTML
  through `WebApplicationFactory`.

`tests/diagnostic-fixtures` is deliberately outside the solution: every project
there fails to compile by design. See its README before adding one.

`tests/msbuild-fixtures` contains projects expected to build successfully under nested real MSBuild.
They are separate from `tests/diagnostic-fixtures`, where every project must fail. The Razor interop
fixtures verify both ProjectReference and isolated NuGet-package delivery, and they inspect generated
source.

`tests/BlazorCodeFirst.TrimTests` and `tests/BlazorCodeFirst.TrimTestApp` live in the
repository but stay outside the solution until the package-based trimming
workflow lands.

`tests/BlazorCodeFirst.WasmPackageApp` is a Blazor WebAssembly app that restores
BlazorCodeFirst as a package, and it is the only consumer here that is both. It
is outside the solution because the package has to exist before it can restore.
Its build is the check; the comment on its `ItemGroup` says what that check is
(#23).

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

# Diagnostics as a real build reports them (packs delivery packages, builds four failure fixtures,
# and runs the Razor interop success fixtures under tests/msbuild-fixtures)
dotnet test tests/BlazorCodeFirst.DiagnosticTests/BlazorCodeFirst.DiagnosticTests.csproj

# The measurements published in DESIGN.md §7.2 (diff cost and component teardown)
dotnet test tests/BlazorCodeFirst.IntegrationTests/BlazorCodeFirst.IntegrationTests.csproj \
  --filter FullyQualifiedName~DiffCostTests --logger "console;verbosity=detailed"

# The measurements published in DESIGN.md §7.1 (allocations per render)
dotnet run -c Release --project tests/BlazorCodeFirst.Benchmarks -- --filter '*'
```

Only the `BlazorCodeFirst.DiagnosticTests` command builds the successful Razor interop fixtures under
`tests/msbuild-fixtures`, the ones that must build rather than fail. The two measurement commands
that follow it in the block above do not, so a break in those fixtures surfaces from the diagnostic
test project alone.

These deliberately omit `--no-build`, which reuses whatever was compiled last and
so reports a pass for code that was never compiled. CI can supply it
(`ci.yml` builds in the preceding step); a local edit-and-test loop cannot.

Every figure in `DESIGN.md` §7.1 and §7.2 comes from the last two commands. Both
compare against something, and both refuse to report a number unless the two
sides render frame-for-frame equivalent output apart from sequence numbers. The
§7.2 comparison asserts that equivalence in `VariantEquivalenceTests`, and the
benchmark exits non-zero from `Program.Main` before BenchmarkDotNet starts. A
number from a mismatched comparison would describe the mismatch, not the
compilation strategy.

No CI step runs either one. A published figure has to be reproducible on demand,
which is a lower bar than a per-PR gate; gating would need a noise threshold and
a failure policy that nothing has decided yet. The §7.2 assertions do ride the
ordinary `dotnet test BlazorCodeFirst.slnx` run, because they are tests. That
follows from where they live, and is not a gate on the numbers.

The benchmark project holds a second measurement set that is **not** published.
`StaticFoldBenchmarks` compares folded markup frames against element frames to
decide #140, and it reports times. §7.1 deliberately does not publish those
times, because the variance is large and machine-dependent. No CI step runs it
either, for the same reason as above. `--filter '*'` picks it up along with the
§7.1 benchmarks; to run only it:

```bash
# The #140 decision input only (not a DESIGN.md figure)
dotnet run -c Release --project tests/BlazorCodeFirst.Benchmarks -- --filter '*StaticFoldBenchmarks*'
```

Its two component pairs were written before the emitter folded, when the element
spelling was the unfolded baseline and the markup spelling hand-wrote the folded
shape with `Html.Raw`. The two sides then rendered deliberately different
frames. Now that the emitter folds, each pair's two sides emit the same frames,
and `Program.Main` gates that equality rather than the strict inequality it once
required: if the counts diverge, the element spelling stopped folding.
`FoldFixtureTests` pins each pair's folded frame count and asserts both sides
render the same DOM.

`ClassChannelBenchmarks` is a third unpublished set. It measures what the class
channel's join allocates, before and after a change to the generation rule that
builds it, and it has no second side. Razor has no additive class channel, so
`Program.Main` has nothing to gate here, and the comparison is this generator
against its own previous output. That is what #236 needed before it could change
the rule at all. `--filter '*'` picks it up; to run only it:

```bash
# The #236 decision input only (not a DESIGN.md figure)
dotnet run -c Release --project tests/BlazorCodeFirst.Benchmarks -- --filter '*ClassChannelBenchmarks*'
```

`Program.Main` also gates one thing that is not a figure. `StaticParityView` and
`StaticParityViewRazor` are a statically written pair, added once folding made
such a comparison possible. Their frame equivalence is what backs `DESIGN.md`
§7.1's statement that the two compilers now emit the same shape for a static
subtree. They carry no benchmark: §7.1's published allocations were measured
against the property-driven pair, and re-spelling those fixtures would
invalidate published numbers.

A new diagnostic needs a fixture shape and an entry in
`DiagnosticExpectations.All`; the coverage guard fails until every descriptor is
listed there or excluded with a reason.

Five projects (the TrimTestApp, the WasmPackageApp,
`msbuild-fixtures/RazorInterop.Package`, and both `diagnostic-fixtures/*.Package`
fixtures) purge an isolated `blazorcodefirst/<version>` NuGet cache before
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

Blazor's browser-side markup path is not covered by `dotnet test`. A text frame
reaches the DOM through `createTextNode`, while a markup frame is parsed by
assigning `innerHTML` on a shared `<template>` element. bUnit parses a document
string with AngleSharp and prerendering writes markup verbatim, so neither sees
the difference. The #140 static fold produces markup frames, so its parity with
the element path is checked in a real browser:

```bash
# Compare folded and unfolded spellings of the same content. The config starts
# the host itself, on port 5100 (macOS ControlCenter holds 5000), and stops it
# afterwards. Set BCF_BASE_URL to point the suite at a host you started some
# other way, which turns that off.
cd tests/BlazorCodeFirst.WebAppTests/browser && npm ci && npx playwright install chromium && npx playwright test

# Playwright's loader transpiles the specs without typechecking them, so this is
# the only thing that reads their types. The `browser` job runs it as well.
npx tsc --noEmit
```

`FoldParityTests` in `BlazorCodeFirst.WebAppTests` (which `dotnet test` does
run) pins the premise this depends on: that each folded container in
`FoldParityView` really collapses to one `AddMarkupContent` frame and each
unfolded container really does not. If that gate is red, the browser result is
worthless even if green, because the comparison would no longer be between a
folded and an unfolded spelling of the same content.

CI runs both of these: the `browser` job in `.github/workflows/ci.yml` starts
the host and invokes Playwright on every pull request. That same run carries
the only coverage a second emission has anywhere.
`SetUpdatesAttributeName` turns on Blazor's DOM resynchronization, which repairs
the divergence created by a two-way binding whose setter normalizes its input:
the element shows what was typed, the render tree holds the normalized value,
and ordinary diffing (comparing the new render tree against the previous one)
writes nothing. bUnit cannot construct that divergence at all, because its
`Input()` writes the value that reaches the setter straight into the AngleSharp
DOM. That was measured, not assumed: an attempt to cover it from bUnit passed
unchanged when the emission was replaced by a no-op. `bind-resync.spec.ts`
measures it against a real browser and `BindResyncTests` pins its premise, in
the same two-part arrangement as above.

`SnapshotCorpusTests` compares the generator's complete emitted source against
baselines committed under `tests/BlazorCodeFirst.Compiler.Tests/Snapshots`, and
that pins sequence numbers and frame order in a way the substring assertions
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
# Pack the runtime and verify its layout, its metadata, and its symbol package
dotnet pack src/BlazorCodeFirst.Runtime/BlazorCodeFirst.Runtime.csproj -c Release -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCodeFirst.$(bash eng/package-version.sh).nupkg

# Publish trimmed and run the trim tests (osx-arm64 shown; linux-x64 also supported)
dotnet publish tests/BlazorCodeFirst.TrimTestApp/BlazorCodeFirst.TrimTestApp.csproj \
  -c Release -r osx-arm64 --self-contained true \
  --configfile tests/BlazorCodeFirst.TrimTestApp/NuGet.config
BLAZORCODEFIRST_TRIM_OUTPUT=$(pwd)/tests/BlazorCodeFirst.TrimTestApp/bin/Release/net10.0/osx-arm64/publish \
  dotnet test tests/BlazorCodeFirst.TrimTests/BlazorCodeFirst.TrimTests.csproj

# Publish the package from a Blazor WebAssembly consumer and check what shipped (#23)
dotnet publish tests/BlazorCodeFirst.WasmPackageApp/BlazorCodeFirst.WasmPackageApp.csproj \
  -c Release --configfile tests/BlazorCodeFirst.WasmPackageApp/NuGet.config
bash eng/verify-wasm-package.sh \
  tests/BlazorCodeFirst.WasmPackageApp/bin/Release/net10.0/publish/wwwroot
```

The WebAssembly publish needs the same `dotnet pack` above it, and no
`wasm-tools` workload. Without one, publish skips the native relink and says so,
which still resolves every `FrameworkReference` and writes the boot manifest —
everything either command reads. Installing the workload only makes the publish
slower and the output smaller. `ci.yml` installs none for the same reason.

Hot Reload against the test host: `dotnet watch --project tests/BlazorCodeFirst.WebAppTestHost/BlazorCodeFirst.WebAppTestHost.csproj`.

To read the `RenderView` the generator actually emitted for a project, which is
the fastest way to confirm what a `Body` lowered to and the only way to see
emitted output a diagnostic does not describe:

```bash
dotnet build <project> -t:Rebuild \
  -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=gen
```

## The package version

`eng/Versions.props` is the only place the version is written. `Directory.Build.props` and
`Directory.Packages.props` both import it, under a condition so the second import is a no-op rather
than an MSB4011. Every project therefore sees `$(BlazorCodeFirstPackageVersion)`, including the
fixtures outside the solution, whose isolated cache paths are built from it.

Nothing else may hold a copy. `eng/package-version.sh` prints what MSBuild resolves, and that is what
`ci.yml` and `release.yml` read to name the packed file. `eng/verify-package.sh` reads
`eng/Versions.props` itself and asserts the packed nuspec agrees, which is the check that the package
carries the version the repository declares. `PackageContentsTests` finds the packed file rather than
composing its name, so it asserts that `dotnet pack` produced exactly one package instead of assuming
which one. Neither README names a version: both install from nuget.org with `--prerelease`, which
stays correct for as long as the version carries a suffix and so needs no edit per release (#297).

## Releasing

`release.yml` runs on a `v*` tag and is staged by reversibility. The first job fails immediately
unless the tag equals `v$(bash eng/package-version.sh)`. It then builds, tests, packs, and verifies,
and uploads the verified package as a workflow artifact so no later job repacks it. The second
creates the GitHub Release. The third pushes to nuget.org and is the only irreversible step, so it
sits behind a GitHub Environment named `nuget.org` that waits for a reviewer.

Two things must exist before the first release, and neither can be created from a pull request: that
environment with a required reviewer, and a `NUGET_API_KEY` secret from an nuget.org key scoped to
*Push new packages and package versions* for the `BlazorCodeFirst` glob.

## Code style

CI runs `dotnet format --verify-no-changes`, which is stricter than the build's
`EnforceCodeStyleInBuild` and fails on any drift. Run it before pushing:

```bash
dotnet format BlazorCodeFirst.slnx --verify-no-changes --no-restore   # check
dotnet format BlazorCodeFirst.slnx                                    # auto-fix
```

Enable the shared pre-push hook once per clone so this runs automatically:
`git config core.hooksPath eng/hooks`. Spell it relative, as written there. Git
resolves a relative value from the top of the working tree, so it survives a
rename or a move of the clone's directory and holds in a linked worktree; an
absolute path stops resolving the moment any of that happens.

Nothing reports it when that happens. Git ignores a `core.hooksPath` naming a
directory that does not exist, silently, and the value lives in `.git/config`
where nothing in the repository can inspect it — a clone with the hook disabled
is indistinguishable from one that has it. The only signal is the hook's own
output, so a push that does not print `pre-push: verifying formatting...` never
ran the check. What that costs is bounded: CI fails on the same drift, one round
trip later.

One Roslyn idiom no tool enforces: ask a `SyntaxTokenList` for a modifier through its own
`Any(SyntaxKind)` overload, never `Enumerable.Any(m => m.IsKind(...))`. The list is a struct the API
hands out by value, so the LINQ shape boxes it and allocates an enumerator on a path the generator
and the analyzers walk for every declaration they visit. Neither `AnalysisLevel=latest-all` nor the
Roslyn SDK's own rules ship a check for it, and `src/BannedSymbols.txt` cannot express it either,
since the symbol it would have to ban is `Enumerable.Any` itself (#215).

## The documentation site

`site/` holds the documentation site and the DocGen tooling that builds its
documents from `site/content`. None of it is in `BlazorCodeFirst.slnx`, so none
of it is covered by the commands above, including the `dotnet format` gate in
§Code style. `.github/workflows/site.yml` is where all of it is enforced
instead:

- DocGen regeneration and a drift check on the three generated files
- DocGen's own unit tests
- `dotnet format` over each of the four projects under `site/`
- an English-only and trailing-newline scan of the tree
- `eng/verify-site-prerender.sh` over the `dotnet publish` output: first that
  the published route set equals the set `site/content` backs, and then, for
  every route in it, the shell, one title element matching what the document's
  own front matter declares, one active nav link, the stylesheet and script
  links, and the absence of the prerendering wrappers and a meta robots tag
- assertions over `404.html`, `robots.txt`, the generated sitemap, and
  `_headers`

Those assertions read the published files as text, which leaves out everything
a browser computes. `site/tests/browser` closes that half with Playwright over
the same publish output, served by a static server the suite starts itself. It
checks:

- that nothing is laid out past the viewport at six widths
- that every clickable label fits the space given to it
- that each text and background pair meets WCAG AA under both a fine and a
  coarse pointer, in each of the two colour schemes
- that both `tokens.css` and the generated `highlight.css` actually answer
  `prefers-color-scheme`, without which the dark half of the pass above would
  re-measure the light palette and report it as green
- that a reader's own choice of scheme lands on the same computed palette the
  system asking for it does, which is what extends that pass to the half of the
  ways this site can be dark that no emulated media query reaches
- that the theme control cycles, persists, overrides the operating system, and
  answers a click with the WebAssembly runtime blocked outright — the last of
  those being why the click is not owned by a Blazor handler
- that the documentation rail sits beside the document above the 60rem
  breakpoint and underneath it below that breakpoint

The routes come from the publish output rather than a list, so a new document
is measured from the commit that adds it.

```bash
# Neither suite produces a publish output; both measure one. Delete it first:
# `dotnet publish` does not clean its output directory, so a route from an
# earlier run survives into the next one and reads as a route nothing backs.
rm -rf site/BlazorCodeFirst.Site/bin/Release/net10.0/publish
dotnet publish site/BlazorCodeFirst.Site/BlazorCodeFirst.Site.csproj -c Release
bash eng/verify-site-prerender.sh site/BlazorCodeFirst.Site/bin/Release/net10.0/publish/wwwroot
cd site/tests/browser && npm ci && npx playwright install chromium && npx playwright test

# As above: the specs run transpiled, never typechecked, so nothing else reads
# their types. `build-deploy` runs it as well.
npx tsc --noEmit
```

A change under `site/content`, under `site/snippets`, or to a file a snippet
reads needs DocGen re-run before the publish, or the publish still carries the
old manifest and the change has no effect:

```bash
dotnet run --project site/tools/BlazorCodeFirst.Site.DocGen.Cli/BlazorCodeFirst.Site.DocGen.Cli.csproj -- \
  site/content \
  site/BlazorCodeFirst.Site/Content/Docs.g.cs \
  site/BlazorCodeFirst.Site/wwwroot/css/highlight.css \
  site/snippets \
  site/BlazorCodeFirst.Site/Content/Snippets.g.cs
```

Every argument is required; the tool prints its usage and exits 1 otherwise. A
snippet may read a file that is also compiled — `/counter`'s figure is
`site/BlazorCodeFirst.Site/Pages/CounterPage.cs` — so editing a page can make
this command's output change. `site/README.md` §Snippets describes the unit.

Two of those checks look obvious and are not, so do not "simplify" them back.
Overflow is measured as element boxes against the viewport rather than as
`document.documentElement.scrollWidth`, because `app.css` sets `overflow-x:
clip` on html and body. The page can never report a wider scroll width, so the
obvious spelling passes on every input. Labels are checked for spilling out of
their own box as well as for wrapping, because the header and footer labels
compute `white-space: nowrap` and can only fail the first way while the rail
links can only fail the second.

`eng/verify-site-prerender.sh` is a script rather than a block inside `site.yml`
so that it can be run against a local publish. That is what makes mutating a
page and watching the check fail affordable, which §Engineering standard
requires of every check in it.

What no site check covers is anything needing a real deployment: Cloudflare's
edge routing, the `_headers` rules as the edge applies them, and behaviour that
appears only after WebAssembly starts. `playwright-cli` is the tool for an
ad-hoc look at a deployed URL; #47 tracks making that a post-deploy step.
`build-deploy` is also not a required check on `main` yet, so a red site build
is visible on a pull request and does not block the merge (#250).

## Issue tracker

Issues carry the current state and the plan. `DESIGN.md` and `ARCHITECTURE.md`
describe the intended finished design and deliberately do not track progress,
so an Issue is the only place a gap, a defect, or a deferred decision is
recorded.

A settled position is not current state, and an Issue is the wrong place to keep
one. Once a question is answered — an alternative rejected, a limit chosen and
measured — the answer lands in a document and the Issue closes: rejected
alternatives in `ARCHITECTURE.md` 付録B, and translation breaks that the surface
deliberately leaves unchecked in 付録D. Closing costs the record nothing, since
a closed Issue stays searchable, linkable, and readable in full. Leaving it open
costs twice: the open list stops meaning "outstanding work", and the record goes
stale in a place nobody re-reads. #74 is the worked example — it named a
precondition, the precondition landed as #176, and the Issue still said the
question was waiting on it.

Every issue gets exactly one `area:` label (the ordering issue below is the one
exception), plus GitHub's default type labels (`bug`, `enhancement`,
`documentation`, `question`, `invalid`) where the type is unambiguous:

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

Order lives in one issue rather than in milestones. A milestone is a set and
cannot hold a sequence, so #298 carries the single ordered list of what to work
on next. It is global rather than one list per milestone: the question it
answers is which issue to start, and that question has one answer at a time.
Only its `Now` section is ranked, and it stays short; the rest is grouped and
promoted into `Now` instead of being ranked in place. It records order, never
dates. #298 is the one issue with no `area:` label, because those name a subject
and it has none.

Diagnostic IDs were renamed from `BC****` to `BCF****` in 2026-08 (#103), along
with the package itself. The four digits did not change, so `BC1001` in an older
issue, commit message, or review comment is today's `BCF1001`. The same change
renamed `ComposeComponentBase` to `BodyComponentBase` and `ComposeLayoutBase` to
`ChromeLayoutBase`.

Four more names changed in 2026-08 (#257), before any of them shipped.
`ComposableAttribute` became `ViewPartAttribute`, so the attribute is written
`[ViewPart]`; `ContentView` became `SlotView`; `ElementBuilder` became
`ElementView`; and `Decorations.Class` takes its argument as `value` rather than
`@class`. The compiler-internal `Composable*` types moved with the attribute, so
a `ComposableRegistry` in an older commit is today's `ViewPartRegistry`, and the
comments that described "a composable" now describe a view part. One name did not
move: the snapshot case `composable-expansion`, because a case name is the
corpus's identity rather than a description of it (`SnapshotCorpusTests`).

## Pull requests

`.github/PULL_REQUEST_TEMPLATE.md` has four sections, each explained in an HTML comment you delete
along with the comment. Two of them are conditional and say so.

Three kinds of "why" meet in a description and only one belongs there. Why the work exists is on the
issue; why the design is the shape it is, once settled, is in `DESIGN.md` or `ARCHITECTURE.md`. Both
have a maintained home, and a copy in a description is the copy nobody updates. The judgements taken
while making this change have no such home: they are about the change rather than the finished
design, they are what a reviewer can still disagree with, and left out of the description they are
nowhere.
`Why and what` asks for those first and the claim second, because the diff already carries what the
code does.

There are no checkboxes, deliberately. This repository is written and reviewed by the same person,
so `[ ] I have read CONTRIBUTING.md` asks the writer to confirm something to themselves, and
`[ ] Tests added` is a weaker version of what CI refuses to merge without. The
conventional-commit prefix already carries the change type, so a "type of change" field would ask for
it twice.

`Verification` applies the same economy to results rather than to reasoning. A repository ruleset
requires `build-test (ubuntu-latest, linux-x64)`, `build-test (macos-latest, osx-arm64)`, and
`browser`, which between them run the build, `dotnet format`, the whole slnx test run, the package
and trim verification, and the browser specs. Those results are on the pull request already, so the
section is for what they cannot reach: the two measurement commands no CI step runs, anything
checked by hand or against a deployment, and `site.yml`, which is not a required check yet (#250). A
command listed there and never run reads exactly like one that was.

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
  parameter setters and stay trimming/AOT safe. `.Template(...)` is held to the
  same standard: the generator writes the `RenderFragment<TContext>` lambda
  itself, so nothing generic is constructed reflectively and the method leaves no
  runtime caller. `TrimmedOutputTests` asserts that absence from metadata, which
  only means something while `TrimTestApp`'s `Body` reaches both overloads.
- Decorator chains collapse into the owning element's emitted attributes rather
  than introducing wrapper nodes or extra frame widths.
- A test that claims to check static folding must pin more than the output it
  asserts: either the frame count, or that the prerender output is empty. Folded
  markup and the element path's `HtmlEncoder` output are identical by
  construction (`ARCHITECTURE.md` §2.7 D), so folding can stop silently and an
  output-only assertion keeps passing. #140 hit four shapes of this. A benchmark
  frame gate required the folded side to emit strictly fewer frames, and stopped
  running anything once both sides became equal. A prerender escaping check
  passed either way, because `HtmlRenderer` escapes ordinary text frames itself.
  A folded/unfolded pair lost its other half when the unfolded side turned
  constant and quietly folded too. A browser gate never reached the
  markup-insertion path at all, because the prerendered content was still what
  the page was showing.
- Preserve bidirectional Razor compatibility. A BlazorCodeFirst component stays a
  plain Blazor component, so a `.razor` file names it as a tag with no same-project
  restriction: what Razor resolves is the hand-written class, and the generator only
  fills in `RenderView`. The other direction, `Component<T>()`, reaches existing Razor
  components. Its type argument must resolve while the generator runs, so a
  `.razor` component declared in the same project cannot be named (`BCF3012`),
  because source generators cannot observe each other's output. `[ViewPart]` has no
  Razor-facing entry point and is not to grow one (`ARCHITECTURE.md` 付録B.4).
- `Component<T>()[children]` binds children to `ChildContent`, mirroring Razor's
  rule that nested content becomes `ChildContent`. `BCF3013` and `BCF3014` fence
  off the shapes that cannot work; 付録A states the exact conditions. `BCF3013`
  requires a settable `[Parameter]` named `ChildContent` of a fragment type, of
  either arity: a `RenderFragment<TContext>` one takes the children with its
  context discarded, which is the outer lambda `.Template`'s context-ignoring
  overload already emits. A generic fragment under any other name is never
  reached through brackets and is always named with `.Template`, so do not widen
  the bracket channel to cover it.
- Value expressions copied into generated code must be lexical-context
  independent, because the generated file carries no `using` directives.
  Resolved type names are normalized to `global::`-qualified names and an
  unresolved one reports `BCF3015`. Keep this separate from `BCF3012`, which is
  reserved for the render-node type argument of `Component<T>()`.
- Diagnostic IDs listed in `AnalyzerReleases.Shipped.md` are published
  specification contracts, so do not repurpose or remove them. An ID recorded in
  `DiagnosticExpectations.RetiredIds` is burned rather than free: it shipped, was
  withdrawn (付録B records why), and must not be handed to a different rule. A
  new diagnostic therefore takes the next number above every allocated *and*
  retired ID. `DiagnosticTableTests` enforces that. New IDs and public
  APIs must be tracked in the corresponding `Unshipped` / `PublicAPI` files or
  the analyzer build gates (RS2000/RS0016) fail.
- `ARCHITECTURE.md` 付録A is the canonical diagnostic table, and
  `DiagnosticTableTests` checks it against `DiagnosticDescriptors` in both
  directions: a descriptor with no row fails, and a row with no descriptor fails
  unless the ID is recorded in
  `DiagnosticExpectations.DocumentedWithoutDescriptor` with the reason it is
  specified ahead of its implementation. The 種別 column is checked against
  `DefaultSeverity`, so changing a diagnostic's severity is a change to the table
  as well. The other prose that names a diagnostic (this file, `DESIGN.md`,
  `site/content`, and the public XML docs) cannot be checked mechanically, so
  update it in the same change.
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

A negative test is not finished until the implementation has been mutated and
the test has been watched to fail. Delete or invert the condition it is supposed
to cover, run it, read the failure, then restore. "Not reported" passes against
an analyzer that reports nothing at all, against a source that never reached the
code under test, and against a condition that some earlier condition already
excluded — and none of those three is visible from the test, from the diff, or
from a green run. The same applies to any sentence that says *why* a shape is
exempt or *what* keeps a check out of some position, whether it is in a comment,
in an expectation's `Note`, or in `ARCHITECTURE.md`. That is a claim about the
implementation, and mutating the implementation is what separates a reason from
a plausible guess.

An expectation derived from build output cannot catch a defect that changes the
build output. The rule above is about mutating the implementation and watching a
test fail; this is the case where the mutation run comes back GREEN, and that
means the expectation moved with the defect rather than that the condition was
unreachable. #278 is the worked example: deleting a check in `DocsNav` published
a route for a document nobody wrote, and an assertion that read "has a
counterpart" out of the published routes was confirmed by the very route the
defect created. Derive the expectation from the source the build reads —
`site/content` — and assert the output against it. Enumerating the output stays
fine, and `eng/verify-site-prerender.sh` does it, but only downstream of the
check that pins the two together.

This is written down because reading was tried first and lost, four times, each
time on code that was already written and already green:

- #155, twice. The `TArgs` type-parameter exclusion was not what kept
  `.On("onclick")` out of BCF3028 (the unsubstituted parameter's `BaseType` is
  `EventArgs`, so the assignability walk accepted it anyway), and branch order
  was not what kept a mistyped handler on a `Fragment(…)` as BCF3008 (Roslyn
  offers no candidate symbols for a receiver no extension method can take).
- #127, once. The type arm of `ShadowedElementHelperScanner` was not what
  excluded CS0119: once the element access fails to bind, the identifier alone
  carries no *symbol* either, so the arm it was credited to was unreachable.
  What it does carry was measured by #266, which is what reaches the type case
  now.
- #68, once, and this one was a test rather than a claim. BCF3029's
  local-function exemption was covered by a source whose enclosing method also
  returned an inert type, so the arm under test could be deleted and the test
  still passed. The mutation found it; the review of the test had not.

Documentation and source comments are written in English. `site/content/ja/` is
the one exception: it holds the Japanese edition of the documentation site,
which is content rather than source. Its generated form in
`site/BlazorCodeFirst.Site/Content/Docs.g.cs` is exempt for the same reason, not
as a second exception.

The rule covers the English documents too, not only `*.cs` and `*.razor`. No
reader-facing string belongs in a page component in any language: each language
declares its own `shell.yml` beside its documents, and DocGen carries it into
the manifest. A sentence that reaches a reader is content, so it lives where the
content does.
