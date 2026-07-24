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
