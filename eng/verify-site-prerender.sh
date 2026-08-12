#!/usr/bin/env bash
# Assertions over the site's `dotnet publish` output: the routes that were prerendered, the shell
# they carry, and the files Cloudflare Pages needs beside them.
#
# A script rather than an inline block in .github/workflows/site.yml, so it can be RUN. A check that
# lives inside a YAML string cannot be watched failing without pushing, and CONTRIBUTING.md
# §Engineering standard does not accept a check nobody has watched fail. eng/verify-package.sh is the
# precedent for the shape.
#
# Sourcing eng/ci-assert.sh rather than hand-rolling grep: its header documents the three shell
# hazards -- errexit's exemption for negated commands, grep's exit 2, and pipefail -- that make an
# absence check silently pass.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: bash eng/verify-site-prerender.sh <path-to-published-wwwroot>" >&2
  exit 1
fi

P=$1

if [ ! -d "$P" ]; then
  echo "Published wwwroot not found: $P" >&2
  exit 1
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
content_root="$repository_root/site/content"

# shellcheck source=eng/ci-assert.sh
. "$script_dir/ci-assert.sh"

# The published document slugs. Defined once and reused below so the three loops over this
# same set (building ROUTES, the active-link/.md check, and the /docs index count check)
# cannot drift from each other. (#46 tracks deriving this from the publish output instead.)
SLUGS="getting-started elements-and-decorations control-flow layouts"

# Every prerendered ROUTE, relative to $P. These carry the shell, exactly one <title>, and
# exactly one active nav link.
ROUTES="index.html counter/index.html docs/index.html"
for slug in $SLUGS; do
  ROUTES="$ROUTES docs/$slug/index.html"
done
# Every prerendered HTML document. 404.html is in this list but not in ROUTES, because no
# nav link points at /404: its active-nav-link expectation is zero, not one.
ALL_HTML="$ROUTES 404.html"

# Attribute order in the generated HTML is class-before-href today; match either order so
# a future emitter change cannot silently turn this guard into a no-op.
assert_active_link() {
  assert_grep \
    "<a[^>]*class=\"nav-link active\"[^>]*href=\"$2\"|<a[^>]*href=\"$2\"[^>]*class=\"nav-link active\"" \
    "$1" "the nav link for '$2' is not marked active"
}

for f in $ROUTES; do
  assert_file "$P/$f" "this route was not prerendered"
  assert_grep 'class="site-nav"' "$P/$f" "the site shell (nav) is missing from this route"
  assert_count 'nav-link active' "$P/$f" 1 "exactly one nav link must be active on a prerendered route"
  assert_count '<title>' "$P/$f" 1 "a prerendered route must render exactly one title element"
done

# A top-level 404.html is what makes Cloudflare Pages serve real 404s. Its documentation is
# explicit: "If your project does not include a top-level 404.html file, Pages assumes that
# you are deploying a single-page application." So if this file ever goes missing, Pages
# silently reverts to answering EVERY unknown URL with 200 and the home page -- an
# unbounded supply of soft 404s and duplicate content on an indexed site. This assertion is
# a production-correctness guard, and doubles as the canary for both the prerender route
# list and the copy target, since nothing links /404.
assert_file "$P/404.html" "the top-level 404.html is missing, so Cloudflare Pages will treat this deployment as a single-page application and answer every unknown URL with 200 and the home page"
assert_grep 'class="site-nav"' "$P/404.html" "the site shell (nav) is missing from 404.html"
assert_count '<title>' "$P/404.html" 1 "404.html must render exactly one title element"
# The BODY, not just the shell. This is the guard that keeps the shared [ViewPart] doing
# its job: 404.html and DocsPage's unknown-slug branch must render the same markup, because
# /docs/unknown is served as this file and then re-rendered by DocsPage on hydration. Without
# it, reverting the else-branch to its own private markup would leave CI green.
assert_grep '<h1>Page not found</h1>' "$P/404.html" "404.html does not render the shared not-found body"
# No nav link corresponds to /404, so nothing may be marked active there.
assert_count 'nav-link active' "$P/404.html" 0 "no nav link may be active on 404.html"
# The prerendered route lands in 404/index.html and is copied out; the directory must be
# gone, or /404/ would serve a second copy of the page.
assert_no_file "$P/404" "the prerendered 404/ directory was not removed after being copied to 404.html"
# The SPA catch-all is what prevented a real 404. The file is deleted, not emptied.
assert_no_file "$P/_redirects" "_redirects is back in the publish output; its catch-all would answer every unknown URL with 200 and the home page"

# The prerendering wrappers must be gone. While they shipped, the prerendered markup was
# inert and hidden under an opaque loader until WASM started, so it reached crawlers only.
for f in $ALL_HTML; do
  assert_not_grep 'b-prerendering' "$P/$f" "a prerendering wrapper div survived into the published HTML, which hides the prerendered content from humans until WASM starts"
  assert_not_grep 'BlazorCodeFirst\.Site\.styles\.css' "$P/$f" "the published HTML links the scoped-CSS bundle, which this project does not produce"

  # All three stylesheet links and the boot script are hand-maintained lines in
  # wwwroot/index.html, not SDK-injected. Every guard above them is an ABSENCE check, so
  # deleting or mistyping one of these four lines would leave every existing assertion
  # green while the deployment ships unstyled, unhighlighted, or fully inert.
  #
  # tokens.css is the worst of the four to lose: app.css reads every colour, font, size and
  # duration through a var() declared there, so without it the page renders with every
  # custom property unresolved -- readable text on a default background, which no other
  # check here would notice.
  assert_grep 'href="css/tokens\.css"' "$P/$f" "the published HTML no longer links css/tokens.css, so every var() in app.css resolves to nothing"
  assert_grep 'href="css/app\.css"' "$P/$f" "the published HTML no longer links css/app.css, so the page ships unstyled"
  assert_grep 'href="css/highlight\.css"' "$P/$f" "the published HTML no longer links css/highlight.css, so code blocks ship unhighlighted"
  assert_grep 'src="_framework/blazor\.webassembly[^"]*\.js"' "$P/$f" "the published HTML no longer loads the Blazor script, so nothing hydrates"

  # The colour-scheme control, whose two halves fail in different directions and are both
  # invisible to every other check here.
  #
  # The inline script applies a stored choice before first paint and owns the click. Lose
  # it and the button renders, looks pressable, and does nothing, while a reader whose
  # choice disagrees with their operating system watches the system's answer flash on
  # every navigation. Lose the button and the script has nothing to listen for. Neither
  # failure changes a single assertion above.
  assert_grep 'data-theme-toggle' "$P/$f" "the published HTML no longer carries the theme control, so a reader cannot choose a colour scheme"
  assert_grep 'localStorage\.getItem' "$P/$f" "the published HTML no longer carries the inline theme script, so a stored choice is neither applied before paint nor changeable"

  # Half of the public switch. A `<meta name="robots" content="noindex, nofollow">` in
  # wwwroot/index.html is what kept this site out of search results before it was
  # published, and restoring that single line would deindex every page again with nothing
  # else in this workflow noticing. The pattern is quote-agnostic and attribute-order
  # agnostic on purpose: for an absence check, matching too much is safe and matching too
  # little is the failure mode.
  #
  # Correct on preview runs too, because preview deindexing does not use this tag: it is an
  # X-Robots-Tag response header injected into _headers further down. That also means this
  # guard must stay a check of the HTML head only -- a `grep -r` over the publish output
  # would hit the injected header on every preview run.
  assert_not_grep '<meta[^>]*name=.?robots' "$P/$f" "the published HTML carries a meta robots tag, which would keep this page out of search results"
done

# The guard above is only correct while NO scoped CSS exists. The SDK does NOT inject the
# <link> for the bundle -- that line is hand-written in index.html -- so if someone adds a
# .razor.css, the bundle appears in the publish output and silently has no effect. Catch the
# premise breaking rather than letting the guard above quietly enforce the wrong thing.
assert_no_glob "$P" '*.styles.css' "a scoped-CSS bundle appeared in the publish output, so a .razor.css was added: restore the <link href=\"BlazorCodeFirst.Site.styles.css\"> line in wwwroot/index.html and drop the guard that forbids it"

# InvariantGlobalization must actually remove the ICU payloads (about 2.6MB uncompressed).
assert_no_glob "$P/_framework" 'icudt*.dat' "ICU data survived the publish, so InvariantGlobalization is not in effect"

assert_active_link "$P/index.html" "/"
assert_active_link "$P/counter/index.html" "/counter"

# The per-route active check is the real guard: if path normalization is wrong, every route
# renders the same active link.
for slug in $SLUGS; do
  f="$P/docs/$slug/index.html"
  assert_active_link "$f" "/docs/$slug"
  assert_not_grep 'href="[^"]*\.md("|#|\?)' "$f" "an un-rewritten .md link survived into the prerendered HTML"
done

# "/docs" is its own index page now, not a second copy of the lowest-order document.
assert_active_link "$P/docs/index.html" "/docs"
assert_grep '<h1>Documentation</h1>' "$P/docs/index.html" "/docs did not render the index page's h1"
# /docs is the only route whose <title> content was otherwise unasserted: the count guard
# above catches a MISSING title, not a WRONG one.
assert_grep '<title>Documentation</title>' "$P/docs/index.html" "the /docs index has the wrong page title"

# The index must enumerate EVERY document. Count 2 per slug, not 1: the documentation rail
# contributes one href per document on this route, so a "contains this href" check would
# pass even if the index body rendered nothing at all. The second occurrence is the index
# list item.
#
# The rail renders on "/docs" and "/docs/{slug}" only -- it is placed by those two pages,
# not by the layout, so the home page and the counter demo no longer carry the whole table
# of contents. That is why this count is asserted here and nowhere else.
for slug in $SLUGS; do
  assert_count "href=\"/docs/$slug\"" "$P/docs/index.html" 2 "the /docs index does not list this document (expected the nav link plus one index entry)"
done

# Content conversion survived the AST pass.
assert_grep 'id="installation"' "$P/docs/getting-started/index.html" "heading ids are missing"
assert_grep 'class="csharp"' "$P/docs/getting-started/index.html" "code highlighting is missing"
assert_grep 'class="headlink"' "$P/docs/control-flow/index.html" "heading permalinks are missing"
assert_grep 'href="/docs/control-flow"' "$P/docs/getting-started/index.html" "a sibling .md link was not rewritten to its SPA route"
assert_grep '<title>Getting Started</title>' "$P/docs/getting-started/index.html" "the per-page title is wrong or missing"
