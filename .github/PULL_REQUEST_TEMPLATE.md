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

<!-- Only what the required checks do not cover.

     `main` requires build-test on both runners and the browser job, all three from ci.yml. The
     build, `dotnet format`, the whole slnx test run, the package and trim verification, and the
     Blazor renderer specs are therefore machine-checked on this pull request before it can merge.
     Repeating those commands here says nothing their results do not, and a copied command line
     that was never run reads exactly like one that was.

     What still belongs here:
       - measurements, since no CI step runs the DESIGN.md §7.1 or §7.2 commands
       - anything checked by hand, in a browser or against a deployment, and what it showed
       - site.yml's result when this touches site/, because build-deploy is not a required check
         yet (#250): its red is advisory, so an edited site/content/*.md whose Docs.g.cs was never
         regenerated will not stop the merge
       - a check that was expected to fail and did not, or the reverse

     "Should pass" is not a result. Delete the section when the required checks genuinely cover
     everything this changed. -->
