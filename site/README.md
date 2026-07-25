# BlazorCompose docs site

Dogfooding target: the documentation site built with BlazorCompose itself,
hosted statically on Cloudflare Pages. See
`docs/superpowers/specs/2026-07-24-dogfood-docs-site-roadmap-design.md`.

This directory is intentionally **outside** `BlazorCompose.slnx` and manages
its own package versions (`ManagePackageVersionsCentrally=false`).

## Run locally (dev)

```
dotnet run --project site/BlazorCompose.Site/BlazorCompose.Site.csproj
```

## Publish (static, trimmed)

```
dotnet publish site/BlazorCompose.Site/BlazorCompose.Site.csproj -c Release
```

Output: `site/BlazorCompose.Site/bin/Release/net10.0/publish/wwwroot`.

## Deploy

CI deploys on push to `main` via `.github/workflows/site.yml`
(`dotnet publish` on GitHub Actions -> `wrangler pages deploy` to Cloudflare Pages).
Cloudflare Pages hosts static assets only; it does not build .NET.

## Docs content pipeline (M3)

Markdown under `site/content/*.md` is converted to HTML at authoring time by the
`DocGen` tool and committed as generated artifacts:

- `site/BlazorCompose.Site/Content/Docs.g.cs` — one `public const string` per doc.
- `site/BlazorCompose.Site/wwwroot/css/highlight.css` — ColorCode class theme.

After editing any `.md`, regenerate and commit the artifacts:

```bash
dotnet run --project site/tools/BlazorCompose.Site.DocGen.Cli/BlazorCompose.Site.DocGen.Cli.csproj -- \
  site/content \
  site/BlazorCompose.Site/Content/Docs.g.cs \
  site/BlazorCompose.Site/wwwroot/css/highlight.css
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
- `title` and `order` are both required, and `order` must be unique across documents.
- Do not write an h1 — the page renders the front matter `title` as the h1.
- Link to sibling documents with `./other.md` (optionally `./other.md#section`). DocGen rewrites
  these to SPA routes and fails the build if the target does not exist. Raw HTML `<a>` tags bypass
  that rewrite, so use Markdown link syntax.
- Headings h2 through h6 automatically get a permalink anchor.
