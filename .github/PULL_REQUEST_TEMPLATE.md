<!-- Closes #N, once per issue this finishes, so the tracker stays the record of what is done.
     An issue that is only touched rather than closed goes in prose instead, so it is not closed by
     accident. -->

## What changed

<!-- One paragraph per closed issue, headed by its number.

     What the code does now. Why the design is what it is belongs on the issue and in DESIGN.md /
     ARCHITECTURE.md, so link to it rather than restating the argument here: a third copy of a decision
     is a third thing that can drift. -->

## Reading order

<!-- Only when the branch has more than one commit worth reading on its own, a feature and its cleanup
     pass for instance. Name the commits and say what each one is for. Otherwise delete this section:
     a single-commit branch is read as the diff. -->

## Not in this PR

<!-- Scope left out on purpose, and review findings deliberately not taken, each with its reason.

     Without this, a finding that was weighed and skipped is indistinguishable from one that was
     missed, and the reader has to re-derive the judgement. Delete the section if there is genuinely
     nothing. -->

## Verification

<!-- The commands from CONTRIBUTING.md §Build and test that were actually run, with their results.
     "Should pass" is not a result.

     Four things this repository fails loudly on, listed because they are the ones that get forgotten:
       - a new diagnostic needs an ARCHITECTURE.md 付録A row and a DiagnosticExpectations entry
         (DiagnosticTableTests and DescriptorCoverageTests each fail without one)
       - a new public Html member needs a KnownSymbols classification (KnownSymbolsSyncTests)
       - new public API needs a PublicAPI.Unshipped.txt entry
       - an edited site/content/*.md needs Docs.g.cs regenerated (site CI fails on the drift) -->
