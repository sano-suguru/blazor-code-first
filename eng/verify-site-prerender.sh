#!/usr/bin/env bash
# Assertions over the site's `dotnet publish` output: the routes that were prerendered, the shell
# they carry, and the files the deployment needs beside them.
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

# The document slugs in one content directory, sorted. Nothing for a directory with no documents.
slugs_in() {
  local dir=$1 f
  for f in "$dir"/*.md; do
    # A glob that matches nothing is left unexpanded, so test the candidate rather than trust it.
    [ -e "$f" ] || continue
    basename "$f" .md
  done | LC_ALL=C sort
}

# "/docs/" for the canonical edition, "/docs/<lang>/" for a translation. Matches Docs.RoutePrefix.
route_prefix() {
  if [ "$1" = "." ]; then
    printf '/docs/'
  else
    printf '/docs/%s/' "$1"
  fi
}

# Every route site/content backs, one line per route as "<kind> <route> <langdir> <slug>".
#
# This is a SECOND, independent derivation of what ComputeDocsPrerenderPaths derives in the csproj
# from the same directory. That is the point: an expectation derived from the publish output cannot
# catch a defect that changes the publish output, and reading the csproj's own answer back would be
# the same mistake one step removed. A defect in that target has to be catchable from here.
#
# Each subdirectory of the content root is a language. Nothing here checks that, and nothing needs
# to: DocGenRunner fails the build on a content subdirectory whose name is not in DocLang.All, and
# the DocGen step runs earlier in this same workflow. A directory that reaches this line is a
# language. The canonical edition has no directory -- its documents are the top-level files -- so it
# is carried as "." and used as a path component unchanged.
#
# "/404" is deliberately absent. CopyPrerendered404 moves that route to a top-level 404.html and
# removes the directory, so it is not an index.html and cannot appear on the published side either.
# The assertions on 404.html further down are what cover it.
expected_table() {
  local lang dir slug

  printf 'fixed / - -\n'
  printf 'fixed /counter/ - -\n'
  printf 'index /docs/ . -\n'

  for slug in $(slugs_in "$content_root"); do
    printf 'doc /docs/%s/ . %s\n' "$slug" "$slug"
  done

  for dir in "$content_root"/*/; do
    [ -d "$dir" ] || continue
    lang=$(basename "$dir")

    # An edition nobody has translated into is not a place in the site: DocsJaIndexPage answers
    # not-found for it, and ComputeDocsPrerenderPaths leaves its index out of the fetch list. Both
    # are the same condition, and this is its third statement.
    [ -n "$(slugs_in "$dir")" ] || continue

    printf 'index %s %s -\n' "$(route_prefix "$lang")" "$lang"
    for slug in $(slugs_in "$dir"); do
      printf 'doc %s%s/ %s %s\n' "$(route_prefix "$lang")" "$slug" "$lang" "$slug"
    done
  done
}

# Every route the publish output contains.
#
# This expression must stay IDENTICAL to the one the "Generate and verify sitemap.xml" step uses to
# build its URL set. The equality asserted below is what lets that step keep deriving from the
# publish output; derived differently here, it would prove the equality of a set the sitemap does
# not read, which is not the claim being made.
published_table() {
  ( cd "$P" && find . -type f -name index.html -print ) \
    | sed -e 's|^\.||' -e 's|index\.html$||' \
    | LC_ALL=C sort
}

# The $P-relative file a route's HTML lands in.
route_file() {
  if [ "$1" = "/" ]; then
    printf 'index.html'
  else
    printf '%sindex.html' "${1#/}"
  fi
}

# Quote a string for use as an extended regular expression.
#
# Every helper in eng/ci-assert.sh passes its pattern to grep -E, where an unescaped "." matches any
# character and "(" opens a group. The sitemap step quotes ORIGIN the same way and for the same
# reason; that one escapes the subset sufficient for a known value, and a heading read out of the
# content tree is not a known value, so this escapes the whole metacharacter set.
ere_quote() {
  printf '%s' "$1" | sed 's/[][\\^$.|?*+(){}]/\\&/g'
}

# A string as the published HTML spells it.
#
# Blazor's HTML encoder does not emit non-ASCII text as itself. Every character of the Japanese
# index heading ships as a numeric character reference: "&#x30C9;", and five more like it. An
# expectation read out of site/content therefore has to be encoded the same way before it can be
# matched, or every translated heading and title fails against a page that is perfectly correct.
#
# perl rather than awk: this needs to decode UTF-8 into codepoints, and the awk on macOS -- where
# this script is run to watch a mutation fail -- is the one-true-awk, which has no multibyte support.
# perl ships with both that machine and the CI image.
#
# The ASCII specials are encoded by that same encoder, and this reproduces it, because a description
# is prose and an apostrophe in prose is ordinary: faq.md carries one. Measured against the published
# HTML rather than assumed -- the encoder writes "&#x27;" for an apostrophe and "&quot;" for a double
# quote, which are two different spellings of the same idea and could not be guessed from each other.
# The instruction this replaces asked for exactly this extension when a heading or title first needed
# it; a description got there first.
html_encode() {
  printf '%s' "$1" | perl -CSDA -pe '
    s/&/&amp;/g;
    s/</&lt;/g;
    s/>/&gt;/g;
    s/"/&quot;/g;
    s/'"'"'/&#x27;/g;
    s/([^\x20-\x7E])/sprintf("&#x%X;", ord($1))/ge'
}

# One key out of a document's or a shell file's front matter.
#
# Bounded to the block between the leading "---" and the next one, rather than matched anywhere in
# the file: a document body is Markdown prose and may legitimately contain a line beginning "title:"
# inside a code fence. Not a YAML parser, and does not need to be -- ShellFile.Parse reads these as a
# flat key/value block too, and pulling a parser in for one field would be a dependency nothing else
# in this workflow has.
front_matter() {
  awk -v key="$2" '
    NR == 1 && $0 == "---" { inside = 1; next }
    inside && $0 == "---" { exit }
    inside && index($0, key ": ") == 1 { print substr($0, length(key) + 3); exit }
  ' "$1"
}

# The origin the published pages name themselves by.
#
# Read out of the published robots.txt rather than taken as an argument: that file already carries it
# in its Sitemap directive, and site.yml already holds that directive to the workflow's own ORIGIN.
# Reading it back here makes canonical, robots.txt and sitemap.xml one chain rather than three
# literals, and it keeps this script runnable against a local publish with nothing to remember.
#
# The one drift this cannot catch is robots.txt and SiteMetadata.Origin disagreeing, which would leave
# every comparison below true against the wrong origin. site.yml asserts that pair against ORIGIN,
# beside its assertion on robots.txt itself.
origin=$(sed -n 's|^Sitemap: \(.*\)/sitemap\.xml$|\1|p' "$P/robots.txt")
if [ -z "$origin" ]; then
  fail "the published robots.txt declares no Sitemap directive, so the origin the pages name themselves by could not be read"
fi

# The card URL is the same on every route, so it is quoted once rather than 26 times.
card_pattern=$(ere_quote "$origin/og.png")

# How many editions of one route site/content backs.
#
# Counted off the route table rather than by walking the content tree again. That table already has one
# row per route per edition, so "how many editions have this document" is how many of its rows there
# are -- and the answer follows slugs_in, which is the one place document discovery is decided. Asked
# any other way, a change to that discovery would move the table and leave this count behind, with the
# gate below still green.
#
# Counted once into $edition_counts rather than per route. expected_table forks a basename per document
# per language, so calling it for each of the 24 routes measured 1.6s and about 1,340 processes against
# 0.07s for one pass -- an eighteen-fold regression on the walk it was written to remove.
#
# Derived rather than fixed at two: every document is translated today, and a document with no
# counterpart names only its own edition.
editions_of() {
  printf '%s\n' "$edition_counts" | awk -v kind="$1" -v slug="$2" '$2 == kind && $3 == slug { print $1 }'
}

# THE GATE. Everything below may enumerate the publish output, because this proved the set it would
# be enumerating is the set site/content backs.
#
# The two directions were measured separately, and they do not have equal weight.
#
# A route the publish output has and site/content does not back is #278, and it is what this gate is
# for. BlazorWasmPreRendering.Build follows links out of the pages it renders, so a page that offers
# a link it should never have offered turns that link into a route that ships -- and then into a
# <loc> in sitemap.xml, handing a search engine a URL for a document nobody wrote. Reproduced by
# adding an English-only document and deleting the counterpart check in DocsNav.Counterparts: the
# switch is offered anyway, the crawler follows it, and "/docs/ja/<slug>" is published for a document
# with no Japanese source.
#
# A route site/content backs and the publish output lacks is a backstop, and a weaker one than it
# looks, because that same crawl backfills. Narrowing the DocMarkdown glob in
# ComputeDocsPrerenderPaths so two documents are left out of the explicit fetch list changes NOTHING
# in the output: the rail links every document from every documentation route, so the crawler finds
# them regardless. What this side does catch was measured with
# BlazorWasmPrerenderingUrlPathRegexToIgnore, which drops a route the crawler did reach -- so it
# fires on prerendering that stopped covering a route, not on a route dropped from the list.
#
# Written here rather than as a helper in eng/ci-assert.sh: every helper there takes one file and one
# ERE, and set equality is neither. The "Verify site source hygiene" step declines to source the
# library for the same reason, and says so.
# The table, built once. Everything below reads this variable rather than calling the function again:
# it forks a basename per document per language, and there are four readers plus a per-route count.
route_table=$(expected_table)

# The edition count of every non-fixed route, as "<count> <kind> <slug>" lines, in one pass. The two
# fixed rows are left out because their branch asserts zero rather than a count.
edition_counts=$(printf '%s\n' "$route_table" \
  | awk '$1 != "fixed" { print $1 " " $4 }' \
  | LC_ALL=C sort \
  | uniq -c)

expected_routes=$(printf '%s\n' "$route_table" | awk '{ print $2 }' | LC_ALL=C sort)
published_routes=$(published_table)

unbacked=$(LC_ALL=C comm -13 <(printf '%s\n' "$expected_routes") <(printf '%s\n' "$published_routes"))
unpublished=$(LC_ALL=C comm -23 <(printf '%s\n' "$expected_routes") <(printf '%s\n' "$published_routes"))

if [ -n "$unbacked" ] || [ -n "$unpublished" ]; then
  echo "--- routes site/content backs ---" >&2
  printf '%s\n' "$expected_routes" >&2
  echo "--- routes the publish output contains ---" >&2
  printf '%s\n' "$published_routes" >&2
  if [ -n "$unbacked" ]; then
    echo "::error::the publish output contains routes no document in site/content backs, so the deployment ships pages nobody wrote and sitemap.xml declares them: $(printf '%s' "$unbacked" | tr '\n' ' ')" >&2
  fi
  if [ -n "$unpublished" ]; then
    echo "::error::site/content backs routes the publish output does not contain, so a document that exists did not ship: $(printf '%s' "$unpublished" | tr '\n' ' ')" >&2
  fi
  exit 1
fi

# Every prerendered HTML document: the routes, plus 404.html. 404.html is not in the route table --
# it is not an index.html, and no nav link points at it, so its active-link expectation is zero
# rather than one -- but every guard in the second loop below is about the shell, and applies to it
# equally.
pages_with_shell() {
  # route_file prints without a trailing newline, because its other caller assigns it to a variable.
  printf '%s\n' "$route_table" | while read -r _ route _ _; do printf '%s\n' "$(route_file "$route")"; done
  echo '404.html'
}

# Attribute order in the generated HTML is class-before-href today; match either order so
# a future emitter change cannot silently turn this guard into a no-op.
assert_active_link() {
  assert_grep \
    "<a[^>]*class=\"nav-link active\"[^>]*href=\"$2\"|<a[^>]*href=\"$2\"[^>]*class=\"nav-link active\"" \
    "$1" "the nav link for '$2' is not marked active"
}

# The href whose nav link must be marked active on a route.
#
# Not the route itself on an index. SiteNav carries ONE "Docs" entry and DocsLinkClass lights it up
# on every language's documentation index, so the active link on "/docs/ja" is href="/docs". Without
# that, "/docs/ja" would be the only prerendered route with no active nav link at all: the rail lists
# documents, so nothing there matches an index either. SiteNav.DocsLinkClass has the reasoning.
active_href() {
  if [ "$1" = "index" ]; then
    printf '/docs'
  elif [ "$2" = "/" ]; then
    printf '/'
  else
    printf '%s' "${2%/}"
  fi
}

# One pass over every route site/content backs, which the gate above proved is every route the
# publish output contains. This replaces three loops over a hardcoded four-slug list (#46): two
# English documents and the whole Japanese edition -- nine of the sixteen routes -- were covered by
# nothing at all, and a document added after the list was written joined them silently.
#
# "$f" holds the $P-relative page path, as it did in the loops this replaces.
#
# Fed by "<<<" rather than by a pipe: a while on the right of a pipe runs in a subshell, where the
# exit 1 inside fail would end the subshell instead of this script.
while read -r kind route langdir slug; do
  f=$(route_file "$route")

  assert_file "$P/$f" "this route was not prerendered"
  assert_grep 'class="site-nav"' "$P/$f" "the site shell (nav) is missing from this route"
  assert_count 'nav-link active' "$P/$f" 1 "exactly one nav link must be active on a prerendered route"
  assert_count '<title>' "$P/$f" 1 "a prerendered route must render exactly one title element"

  # What the route says about itself to a search engine and to a social card. SiteMeta emits all of
  # it, so the count guards catch a page that lost the component and the value guards catch one that
  # was handed the wrong route. The route table already carries the trailing-slash form sitemap.xml
  # declares, so it is compared verbatim: Workers redirects the bare form, and a canonical without
  # the slash would name a URL that answers 307 rather than the page.
  assert_count '<link rel="canonical"' "$P/$f" 1 "a prerendered route must declare exactly one canonical link"
  assert_grep "<link rel=\"canonical\" href=\"$(ere_quote "$origin$route")\"" "$P/$f" "this route's canonical link does not name the route itself"
  assert_grep "<meta property=\"og:url\" content=\"$(ere_quote "$origin$route")\"" "$P/$f" "this route's og:url does not name the route itself"
  assert_count '<meta name="description"' "$P/$f" 1 "a prerendered route must declare exactly one description"
  assert_count '<meta property="og:image"' "$P/$f" 1 "a prerendered route must declare exactly one card image"
  assert_grep "<meta property=\"og:image\" content=\"$card_pattern\"" "$P/$f" "this route's card image is not the one this origin serves"

  # One alternate per edition plus an x-default, or none at all. SiteMeta.Tags carries the reason a
  # set is all-or-nothing; this counts what arrived.
  #
  # Asked of "$kind" rather than of the route's shape. The table already sorts the routes into fixed,
  # index and doc, so matching "/docs/*" a second time would be a second answer to a question already
  # answered -- and the two would disagree the day a fixed route is served from under /docs.
  #
  # The x-default count is not subsumed by the total: dropping the x-default while duplicating an
  # edition's link satisfies the total and leaves a reader whose language has no edition offered
  # nothing.
  #
  # The zero-alternates branch is a fact about the two routes expected_table hardcodes, not a rule
  # about non-documentation routes: "/" and "/counter/" have one edition each. A translated route
  # outside /docs would pass it while carrying no hreflang set at all, which is the defect the other
  # branch exists to catch -- so adding one means giving expected_table an expected count per route
  # and reading it here, rather than extending this branch.
  if [ "$kind" = "fixed" ]; then
    assert_count 'rel="alternate" hreflang=' "$P/$f" 0 "the home page and the counter demo have one edition each, so neither may name an alternate"
  else
    assert_count 'rel="alternate" hreflang=' "$P/$f" "$(( $(editions_of "$kind" "$slug") + 1 ))" "this documentation route does not name one alternate per edition plus an x-default"
    assert_count 'hreflang="x-default"' "$P/$f" 1 "this documentation route declares no x-default alternate, so a reader whose language has no edition is offered nothing"
  fi

  # The per-route active check is the real guard: if path normalization is wrong, every route
  # renders the same active link.
  assert_active_link "$P/$f" "$(active_href "$kind" "$route")"

  if [ "$kind" = "doc" ]; then
    assert_not_grep 'href="[^"]*\.md("|#|\?)' "$P/$f" "an un-rewritten .md link survived into the prerendered HTML"

    # The count guard above catches a MISSING title element. This catches a WRONG one, on every
    # document route rather than on the single route that used to be spelled out at the bottom of
    # this script. The empty check is not defensive noise: an empty expectation would make the
    # pattern "<title></title>", which passes on nothing and fails on everything.
    title=$(front_matter "$content_root/$langdir/$slug.md" title)
    if [ -z "$title" ]; then
      fail "site/content/$langdir/$slug.md declares no 'title' in its front matter, so this route's title could not be checked"
    fi
    assert_grep "<title>$(ere_quote "$(html_encode "$title")")</title>" "$P/$f" "this document's title element does not match the 'title' in its front matter"

    # The count guard above catches a MISSING description. This catches a WRONG one, on the same
    # terms the title is caught on: read out of front matter, encoded as the published HTML spells it,
    # matched whole. Without it, every document could ship the same sentence.
    description=$(front_matter "$content_root/$langdir/$slug.md" description)
    if [ -z "$description" ]; then
      fail "site/content/$langdir/$slug.md declares no 'description' in its front matter, so this route's description could not be checked"
    fi
    assert_grep "<meta name=\"description\" content=\"$(ere_quote "$(html_encode "$description")")\"" "$P/$f" "this document's description does not match the 'description' in its front matter"
    assert_grep "<meta property=\"og:description\" content=\"$(ere_quote "$(html_encode "$description")")\"" "$P/$f" "this document's card description does not match the 'description' in its front matter"
  fi

  if [ "$kind" = "index" ]; then
    heading=$(front_matter "$content_root/$langdir/shell.yml" index-title)
    if [ -z "$heading" ]; then
      fail "site/content/$langdir/shell.yml declares no 'index-title', so this index's heading could not be checked"
    fi
    # "/docs" is its own index page, not a second copy of the lowest-order document, and each
    # language has one. Both strings come from that language's shell.yml rather than from a literal
    # here, which is what lets this reach the Japanese index without writing Japanese into a
    # workflow that scans itself for it.
    assert_grep "<h1>$(ere_quote "$(html_encode "$heading")")</h1>" "$P/$f" "this documentation index did not render the heading its shell.yml declares"
    assert_grep "<title>$(ere_quote "$(html_encode "$heading")")</title>" "$P/$f" "this documentation index has the wrong page title"

    # The index has no document to take a description from, so its shell file declares one.
    lead=$(front_matter "$content_root/$langdir/shell.yml" index-description)
    if [ -z "$lead" ]; then
      fail "site/content/$langdir/shell.yml declares no 'index-description', so this index's description could not be checked"
    fi
    assert_grep "<meta name=\"description\" content=\"$(ere_quote "$(html_encode "$lead")")\"" "$P/$f" "this documentation index's description does not match the 'index-description' in its shell.yml"
    assert_grep "<meta property=\"og:description\" content=\"$(ere_quote "$(html_encode "$lead")")\"" "$P/$f" "this documentation index's card description does not match the 'index-description' in its shell.yml"

    # The index must enumerate EVERY document in its own edition. Count 2 per slug, not 1: the
    # documentation rail contributes one href per document on this route, so a "contains this href"
    # check would pass even if the index body rendered nothing at all. The second occurrence is the
    # index list item.
    #
    # The rail renders on an index and on a document only -- DocsView places it, not the layout, so
    # the home page and the counter demo do not carry the whole table of contents. That is why this
    # count is asserted here and nowhere else.
    #
    # No ERE quoting on the href: site/README.md constrains a slug to ^[a-z0-9]+(-[a-z0-9]+)*\z and
    # DocSlug enforces it at build time, so no metacharacter can reach this pattern.
    for indexed in $(slugs_in "$content_root/$langdir"); do
      assert_count "href=\"$(route_prefix "$langdir")$indexed\"" "$P/$f" 2 "this index does not list one of its edition's documents (expected the rail link plus one index entry)"
    done
  fi
done <<< "$route_table"

# A top-level 404.html is the page an unmatched request is answered with: site/wrangler.jsonc sets
# not_found_handling to 404-page, which serves the nearest 404.html and nothing else. Losing the
# file leaves every unknown URL on an indexed site answering with an empty body. This assertion is
# a production-correctness guard, and doubles as the canary for both the prerender route list and
# the copy target, since nothing links /404.
#
# It was written when the site deployed to Cloudflare Pages, where the same file did more: Pages
# inferred a single-page application from its ABSENCE and then answered every unknown URL with 200
# and the home page. Workers reads the configuration instead of the file listing, so that failure
# mode is gone and this check is no longer the only thing standing between the site and it.
assert_file "$P/404.html" "the top-level 404.html is missing, so every unknown URL will answer with an empty 404 instead of the not-found page"
assert_grep 'class="site-nav"' "$P/404.html" "the site shell (nav) is missing from 404.html"
assert_count '<title>' "$P/404.html" 1 "404.html must render exactly one title element"
# The BODY, not just the shell. This is the guard that keeps the shared [ViewPart] doing
# its job: 404.html and DocsPage's unknown-slug branch must render the same markup, because
# /docs/unknown is served as this file and then re-rendered by DocsPage on hydration. Without
# it, reverting the else-branch to its own private markup would leave CI green.
assert_grep '<h1>Page not found</h1>' "$P/404.html" "404.html does not render the shared not-found body"
# No nav link corresponds to /404, so nothing may be marked active there.
assert_count 'nav-link active' "$P/404.html" 0 "no nav link may be active on 404.html"
# The one route that says nothing about itself. _headers answers it with X-Robots-Tag: noindex, so a
# canonical would name a URL for a page that must not be indexed, and a card would offer a share
# preview for an error. SiteMeta is deliberately absent from NotFoundContent; this holds it absent.
assert_count '<link rel="canonical"' "$P/404.html" 0 "404.html declares a canonical link, but the not-found page must not be indexed"
assert_count '<meta name="description"' "$P/404.html" 0 "404.html declares a description, but the not-found page must not be indexed"
assert_count '<meta property="og:' "$P/404.html" 0 "404.html declares Open Graph tags, but an error page must not offer a share preview"
assert_count 'rel="alternate" hreflang=' "$P/404.html" 0 "404.html names hreflang alternates, but it is not one edition of a page that has others"
# The prerendered route lands in 404/index.html and is copied out; the directory must be
# gone, or /404/ would serve a second copy of the page.
assert_no_file "$P/404" "the prerendered 404/ directory was not removed after being copied to 404.html"
# The SPA catch-all is what prevented a real 404. The file is deleted, not emptied.
assert_no_file "$P/_redirects" "_redirects is back in the publish output; its catch-all would answer every unknown URL with 200 and the home page"

# The prerendering wrappers must be gone. While they shipped, the prerendered markup was
# inert and hidden under an opaque loader until WASM started, so it reached crawlers only.
while read -r f; do
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

  # The whole privacy half of #252. The fonts are served from this origin now, and nothing else
  # here or in the browser suite reads font-family or a request log: restoring the Google Fonts
  # <link> -- or leaving a preconnect behind after it -- would announce every visitor to a third
  # party before first paint with every other assertion in this file still green.
  assert_not_grep 'fonts\.googleapis\.com' "$P/$f" "the published HTML contacts fonts.googleapis.com again, which announces every visitor to a third party before first paint"
  assert_not_grep 'fonts\.gstatic\.com' "$P/$f" "the published HTML contacts fonts.gstatic.com again, which announces every visitor to a third party before first paint"
done <<< "$(pages_with_shell)"

# The guard above is only correct while NO scoped CSS exists. The SDK does NOT inject the
# <link> for the bundle -- that line is hand-written in index.html -- so if someone adds a
# .razor.css, the bundle appears in the publish output and silently has no effect. Catch the
# premise breaking rather than letting the guard above quietly enforce the wrong thing.
assert_no_glob "$P" '*.styles.css' "a scoped-CSS bundle appeared in the publish output, so a .razor.css was added: restore the <link href=\"BlazorCodeFirst.Site.styles.css\"> line in wwwroot/index.html and drop the guard that forbids it"

# The four faces the page draws in. They are ordinary wwwroot files and can be lost the way any file
# can, and the version in each name means a rename is a silent miss rather than a compile error. The
# browser suite fails too, on the unavailable face -- it says a family is missing, this says the file
# is. Named one by one rather than counted, because the name is the half that goes wrong.
for font in geist-1.800-latin.woff2 geist-1.800-latin-ext.woff2 \
            jetbrains-mono-2.211-latin.woff2 jetbrains-mono-2.211-latin-ext.woff2; do
  assert_file "$P/fonts/$font" "the publish output has no fonts/$font, so every page naming that face falls back to a system font"
done

# InvariantGlobalization must actually remove the ICU payloads (about 2.6MB uncompressed).
assert_no_glob "$P/_framework" 'icudt*.dat' "ICU data survived the publish, so InvariantGlobalization is not in effect"

# Content conversion survived the AST pass. Spot checks against literals, not derived from
# anything: these are about what DocGen's Markdown conversion produces, which no file in
# site/content states.
assert_grep 'id="installation"' "$P/docs/getting-started/index.html" "heading ids are missing"
assert_grep 'class="csharp"' "$P/docs/getting-started/index.html" "code highlighting is missing"
assert_grep 'class="headlink"' "$P/docs/control-flow/index.html" "heading permalinks are missing"
assert_grep 'href="/docs/control-flow"' "$P/docs/getting-started/index.html" "a sibling .md link was not rewritten to its SPA route"
