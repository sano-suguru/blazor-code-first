# Diagnostic fixtures

Deliberately broken projects, built by real MSBuild from
`tests/BlazorCodeFirst.DiagnosticTests`. Nothing else builds them: they are outside
`BlazorCodeFirst.slnx`, so a solution build, `dotnet format`, and CI's build step never see them.
Each one is expected to fail to compile. That is the point.

They exist because every other diagnostic test in the repository instantiates the generator or an
analyzer directly through an in-memory Roslyn host. That verifies the *logic* of a diagnostic and
says nothing about whether it reaches an author building an ordinary project. Issue #76 is what
that gap costs: BCF1001 was correct, tested, and unreportable in a real build.

## Why four projects and not one

Two independent axes, and the combinations are not interchangeable.

**Reporting mechanism.** The generator driver and the analyzer driver fail independently. csc does
not run the analyzer driver at all when the compilation has a declaration-level error, so an
analyzer diagnostic can only be asserted in a compilation that has none. That is why the
`AnalyzerDelivery.*` fixtures contain exactly one broken component and nothing else: adding any of
the shapes from `GeneratorDelivery.*` would suppress the diagnostic under test for reasons that
have nothing to do with it. See `ARCHITECTURE.md` 付録A.0.

The same cutoff also disqualifies C# errors as a way to state a BlazorCodeFirst constraint. csc stops
after the declaration stage when the compilation has a declaration-level error, so it never binds
method bodies, and a component whose design-time expression fails to translate always has one, so the
CS0534 from the `RenderView` that was never generated. Any body-binding error inside that expression
is therefore computed for a build that has already been abandoned, and never reaches the author.
`Compilation.GetDiagnostics()`, which every in-process test calls, binds bodies unconditionally and
does not reproduce the cutoff, so an in-process test that observes a C# error is not evidence that
the error is delivered. BCF3008 is the case that proved it: its retirement in favour of the CS1929
that the type system genuinely raises was measured here and found to leave the author with nothing
but CS0534 and a generic BCF1003, so the detection was restored as a generator diagnostic.

**Delivery path.** `ProjectReference … OutputItemType="Analyzer"` (what this repository's own consumer projects use)
and the packed `analyzers/dotnet/cs/` layout (what an external consumer gets) are different paths
to the same DLL, and `eng/verify-package.sh` only asserts that the file is *in* the package.

| Fixture | Mechanism | Delivery |
| --- | --- | --- |
| `AnalyzerDelivery.ProjectReference` | analyzer driver (BCF3001) | ProjectReference |
| `GeneratorDelivery.ProjectReference` | generator driver (everything else) | ProjectReference |
| `AnalyzerDelivery.Package` | analyzer driver | NuGet package |
| `GeneratorDelivery.Package` | generator driver | NuGet package |

Each analyzer fixture also declares a type in the global namespace, which violates CA1050. That
unrelated rule is the control: it must be reported in the analyzer fixtures (proving the analyzer
driver ran at all) and must be absent from the generator fixtures (pinning the suppression
behaviour the whole design follows from).

## Adding a diagnostic

Add the shape to `GeneratorDelivery.ProjectReference` (or to a new analyzer fixture if it is
analyzer-reported), then assert it in `DiagnosticDeliveryTests`. `DescriptorCoverageTests` fails
until every descriptor is either asserted there or listed in its exclusion table with a reason, so
a new diagnostic cannot be dead on arrival the way BCF1001 was.
