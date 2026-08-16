# BlazorCodeFirst docs site

Dogfooding target: the documentation site built with BlazorCodeFirst itself,
hosted statically on Cloudflare Workers. Outstanding work on the site is tracked
under the `area: site` label in Issues.

This directory is intentionally outside `BlazorCodeFirst.slnx` and manages
its own package versions (`ManagePackageVersionsCentrally=false`).

## Run locally (dev)

```
dotnet run --project site/BlazorCodeFirst.Site/BlazorCodeFirst.Site.csproj
```

## Publish (static, trimmed)

```
dotnet publish site/BlazorCodeFirst.Site/BlazorCodeFirst.Site.csproj -c Release
```

Output: `site/BlazorCodeFirst.Site/bin/Release/net10.0/publish/wwwroot`.

## Deploy

CI deploys on push to `main` via `.github/workflows/site.yml`
(`dotnet publish` on GitHub Actions -> `wrangler deploy` to a Worker whose static
assets are that publish output, configured in `site/wrangler.jsonc`). The Worker
runs no script and does not build .NET.

## Stylesheets

Three stylesheets, linked by hand from `wwwroot/index.html`. CI asserts all three links, because
losing one leaves every other assertion in `site.yml` green while the deployment ships unstyled.

- `wwwroot/css/tokens.css` — the design system: every colour (OKLCH), font, size, space, radius,
  easing, duration and layer the site uses, declared once on `:root`. The comment at the top of the
  file is the durable record of the design's shape (macrostructure, palette, type, nav and footer
  archetypes).
- `wwwroot/css/app.css` — the rules. Every value in it is a `var()` from `tokens.css`; a literal
  colour or font stack there is a defect, not a shortcut.
- `wwwroot/css/highlight.css` — generated, see below.

## Docs content pipeline

Markdown under `site/content/*.md` is converted to HTML at authoring time by the
`DocGen` tool and committed as generated artifacts:

- `site/BlazorCodeFirst.Site/Content/Docs.g.cs`: a `DocEntry` record per doc plus a `Docs` manifest.
  `Docs.All` is an `ImmutableArray<DocEntry>` ordered by front matter `order` with ties broken by
  slug, and `Docs.Find(slug)` is a case-insensitive lookup.
- `site/BlazorCodeFirst.Site/wwwroot/css/highlight.css`: the ColorCode class theme, repainted onto
  the site's palette by `ColorCodeTheme` — once for paper and again for the dark page, emitted as
  `light-dark()` pairs rather than a `prefers-color-scheme` block so they answer a reader's own
  choice of scheme and not only the operating system. That file is the one place a colour is
  written outside `tokens.css`, and it says why.

After editing any `.md` or any snippet source, regenerate and commit the artifacts:

```bash
dotnet run --project site/tools/BlazorCodeFirst.Site.DocGen.Cli/BlazorCodeFirst.Site.DocGen.Cli.csproj -- \
  site/content \
  site/BlazorCodeFirst.Site/Content/Docs.g.cs \
  site/BlazorCodeFirst.Site/wwwroot/css/highlight.css \
  site/snippets \
  site/BlazorCodeFirst.Site/Content/Snippets.g.cs
```

CI regenerates and fails on drift (`git diff --exit-code`). The app build does not
run the tool; `Docs.g.cs` is compiled as ordinary committed source.

## Snippets

A snippet is one code figure on a page that is not a document. `site/snippets/manifest` declares
`name: path` for each, and DocGen converts the file behind each name into highlighted HTML at
authoring time, emitting `BlazorCodeFirst.Site/Content/Snippets.g.cs`. A page places one with
`Raw(Snippets.<Name>)`.

The conversion is the documents' own pipeline: the source is wrapped in a Markdown fence, whose
language comes from the file's extension, and handed to the same `MarkdownConverter`. Figures and
prose code blocks therefore carry the same ColorCode classes, and `HighlightCssEmitterTests` holds
both to `css/highlight.css`. The map names `.cs` and `.html`; an extension it does not name is a
build error, because a figure that silently lost its highlighting reads as a theme regression rather
than a manifest mistake.

`.html` is there for the output half of a pair. Its seven scopes are repainted onto the same four
roles the C# half uses, so `<div>` carries the colour `Div` does and an attribute value the colour
its string literal does — the pair reads as one correspondence rather than as two languages.
`ColorCodeTheme` says which and why. Inheriting them was not an option and the parity check would
not have said so: ColorCode's own dictionaries already carry every HTML scope, so the rules exist
and only their colour is wrong. `HighlightCssEmitterTests` names the seven.

A declared path may leave `site/snippets/`, which is how `/counter`'s figure reads
`BlazorCodeFirst.Site/Pages/CounterPage.cs` — the file the page is compiled from. The freshness gate
above covers `Snippets.g.cs` too, so a figure cannot fall behind the source it was read from.

Snippets live beside `content/` rather than inside it because every subdirectory of `content/` names
the language of the documents in it, and DocGen rejects one that does not.

One landing-page figure is not a snippet. The build error in
`BlazorCodeFirst.Site/Content/CodeSamples.cs` stays hand-written, because it is a terminal message
rather than code, and it carries `.diag-loc` and `.diag-id` instead of the code classes.

Two landing-page figures claim to be compiler output: that error quotes BCF3016's `messageFormat`,
and `site/snippets/generated.cs` claims to be the frames the generator emits for `design-time.cs`.
`LandingPageFigureTests` holds both against the compiler. It lives in
`tests/BlazorCodeFirst.Compiler.Tests` because nothing under `site/` can reference the compiler, so
editing either figure is checked by `dotnet test BlazorCodeFirst.slnx` and not by `site.yml`.

A third pair claims something the site can check for itself. `site/snippets/hero.cs` and
`site/snippets/hero.html` say that one expression produces that markup, and `FigureTests` in
`site/tests` holds both to `BlazorCodeFirst.Site/Content/Examples/Hero.cs`: the C# half against the
source, the HTML half against what rendering it produces. That is a claim about the runtime rather
than about the generator, so it is answerable from this side of the `slnx` boundary and `site.yml`
covers it.

An example under `Content/Examples/` declares what the figure shows by wrapping it in `// <figure>`
and `// </figure>`, and `FigureTests` compares the figure to that region dedented. The markers are
needed because a component carries `using` directives and a class declaration that the figure does
not show, and they are read rather than assumed: a source with no region throws by name instead of
producing a diff against the whole file. These types are `internal` and reached through the
`InternalsVisibleTo` in the app's project file. Nothing on the site renders one — the landing page
places the figure's text, not the component — so CA1515 is right that they should not be public.

### Authoring a document

Add `site/content/<slug>.md` with front matter, then regenerate the committed artifacts:

````markdown
---
title: My Page
order: 40
---

## First section
````

- The file name becomes the `/docs/<slug>` route, so it must match `^[a-z0-9]+(-[a-z0-9]+)*\z`
  (lowercase words separated by single hyphens).
- A subdirectory of `site/content/` names the language of the documents in it. `ja` is the only one
  recognized; any other directory is a build error. The top-level files are the canonical English
  edition.
- `title` and `order` are both required, and `order` must be unique within a language. A translation
  shares the order of the document it translates, so the two sit at the same position in their own
  navigations.
- `order` decides the position of the document in the navigation and in the `/docs` index. It no
  longer changes what any URL renders: `/docs` is its own index page.
- Do not write an h1. The page renders the front matter `title` as the h1.
- Link to sibling documents with `./other.md` (optionally `./other.md#section`). DocGen rewrites
  these to SPA routes and fails the build if the target does not exist. The target must exist in the
  linking document's own language, so a translation cannot link a reader out of its edition. Raw
  HTML `<a>` tags bypass that rewrite, so use Markdown link syntax.
- Headings h2 through h6 automatically get a permalink anchor.
- A `|`-delimited table is a supported construct. See §Tables below for why, and for what the
  stylesheet does with one.

### Tables

Markdig's pipe-table extension is registered, so a `|`-delimited table renders as a `<table>`.
`.prose table` in `css/app.css` makes it a horizontal scroll container.

It was not always. The extension was off while that stylesheet rule was already written, so a table
an author wrote reached the reader as literal pipes and nothing said so (#333). The two ways out
were to enable the extension or to reject a table the way `MarkdownBodyRules.EnsureNoTopLevelHeading`
rejects an h1. Enabling it, because rejecting it cannot be done as well:

- The h1 rule reads the parsed document, and the comment on it records why a text scan cannot. A
  table rule would have had to be that text scan, because with the extension off the parser produces
  no table node to match — and a `|` is ordinary inside a C# fence, so the scan would have to
  re-derive fence tracking to avoid rejecting code. With the extension on the fence parser claims
  the block first, which `ToHtml_PipesInsideAFence_StayCode` holds.
- Nothing in the design forbids a table. The h1 rule exists because front matter `title` owns the
  page title and a body h1 would let the two drift; a table has no such conflict.

The scroll container is a guard rather than something today's content exercises: the table in
`components-and-reuse.md` fits its box at every width the browser suite uses. `layout.spec.ts`
widens a cell at runtime to measure the rule, and says there why.

### Translating a document

Add `site/content/ja/<slug>.md`, using the same slug as the English document it follows. A
translation with no English counterpart is a build error: the canonical language leads.

Its front matter carries a required `source-hash` in addition to `title` and `order`: the first 8
hex digits of `SHA-256(title + LF-normalized body)` of the English document. DocGen compares it and

- on a match, the page renders normally;
- on a mismatch, the page carries a notice linking to the English document, and DocGen prints the
  hash to paste back once the translation has actually been revised. **The build still succeeds.**
  An English edit must not oblige the same commit to rewrite every translation;
- when absent or malformed, the build fails. A missing hash would read as up to date rather than as
  unchecked.

Front matter `order` is left out of the hash on purpose: renumbering the navigation changes no word
a reader sees.

Paste the new hash by hand after revising. There is no `--update-hashes` flag, deliberately: it
would let a stamp land without a revision behind it.

Wrap the body to the same column as the rest of the tree. A soft line break renders as a space, so
DocGen drops the one that falls between two CJK characters and keeps the one beside a Latin word, a
number, or a code span: a wrapped Japanese sentence reaches the reader as it was written, and the
spaces this edition sets around an inline `<code>` survive. A hard break (two trailing spaces) is
still a `<br>`. `MarkdownConverterTests` holds each of those boundaries.

### Shell text

Every language that has documents declares a `shell.yml` beside them, the English edition included
(`site/content/shell.yml`). It holds the strings the documentation shell shows, so no page component
contains a sentence in any language:

````yaml
---
name: English
index-title: Documentation
index-lead: Every document in the guide, in reading order.
rail-heading: Guide
language-label: Language
---
````

`name` is what the language calls itself, shown in the language switch to a reader of a different
edition. A translation additionally declares `stale-notice` and `stale-link`, which the canonical
language must not declare. Every key is required; there is no fallback to English, because one
English word among translated ones is exactly what nobody would notice.
