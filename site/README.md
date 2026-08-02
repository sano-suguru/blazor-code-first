# BlazorCodeFirst docs site

Dogfooding target: the documentation site built with BlazorCodeFirst itself,
hosted statically on Cloudflare Pages. Outstanding work on the site is tracked
under the `area: site` label in Issues.

This directory is intentionally **outside** `BlazorCodeFirst.slnx` and manages
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
(`dotnet publish` on GitHub Actions -> `wrangler pages deploy` to Cloudflare Pages).
Cloudflare Pages hosts static assets only; it does not build .NET.

## Docs content pipeline

Markdown under `site/content/*.md` is converted to HTML at authoring time by the
`DocGen` tool and committed as generated artifacts:

- `site/BlazorCodeFirst.Site/Content/Docs.g.cs` — a `DocEntry` record per doc plus a `Docs` manifest:
  `Docs.All` (an `ImmutableArray<DocEntry>` ordered by front matter `order`, ties broken by slug)
  and `Docs.Find(slug)` (case-insensitive lookup).
- `site/BlazorCodeFirst.Site/wwwroot/css/highlight.css` — ColorCode class theme.

After editing any `.md`, regenerate and commit the artifacts:

```bash
dotnet run --project site/tools/BlazorCodeFirst.Site.DocGen.Cli/BlazorCodeFirst.Site.DocGen.Cli.csproj -- \
  site/content \
  site/BlazorCodeFirst.Site/Content/Docs.g.cs \
  site/BlazorCodeFirst.Site/wwwroot/css/highlight.css
```

CI regenerates and fails on drift (`git diff --exit-code`). The app build does not
run the tool; `Docs.g.cs` is compiled as ordinary committed source.

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
- `site/content/` is flat: only top-level `*.md` files become documents. A file placed in a
  subdirectory is silently ignored, not reported as an error.
- `title` and `order` are both required, and `order` must be unique across documents.
- `order` decides the position of the document in the navigation and in the `/docs` index. It no
  longer changes what any URL renders: `/docs` is its own index page.
- Do not write an h1 — the page renders the front matter `title` as the h1.
- Link to sibling documents with `./other.md` (optionally `./other.md#section`). DocGen rewrites
  these to SPA routes and fails the build if the target does not exist. Raw HTML `<a>` tags bypass
  that rewrite, so use Markdown link syntax.
- Headings h2 through h6 automatically get a permalink anchor.
