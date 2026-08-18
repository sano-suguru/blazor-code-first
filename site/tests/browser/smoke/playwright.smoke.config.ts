import { defineConfig } from '@playwright/test';

/**
 * Post-deploy smoke checks (#47), run against a live deployment rather than a local publish output.
 *
 * The sibling suite one directory up measures overflow, wrapping, and colour against a
 * `dotnet publish` output served locally, and deliberately does not need a deployment (see its own
 * config). What that suite cannot see is hydration behaviour and Cloudflare's edge routing, which
 * exist only once something is actually deployed: whether the client render still produces the
 * document a route names rather than silently falling back to the not-found content, whether the
 * WebAssembly boot throws, whether replacing the prerendered markup shifts the layout, and whether
 * an unmapped path gets a real 404 rather than Cloudflare treating the deployment as a single-page
 * application and answering with the home page (the failure mode #44 names).
 *
 * BCF_SITE_BASE_URL is required and there is no fallback to a local server. The sibling suite falls
 * back to `http://localhost:5200` because a missing env var there just means "run locally, as
 * usual"; here it would mean silently testing the wrong thing under the name of a check that exists
 * specifically to test a deployment, which is the #163 silent-pass shape this repository does not
 * tolerate.
 */

const baseURL = process.env.BCF_SITE_BASE_URL;
if (!baseURL) {
  throw new Error(
    'BCF_SITE_BASE_URL is required to run the post-deploy smoke suite -- it names the deployment ' +
      'to test and this suite refuses to fall back to a local server. Example: ' +
      'BCF_SITE_BASE_URL=https://pr-123-blazor-code-first-site.snsgr.workers.dev npx playwright test --config=smoke/playwright.smoke.config.ts',
  );
}

export default defineConfig({
  testDir: '.',
  retries: 0,
  fullyParallel: true,
  // NOT '100%', unlike the sibling config. Every test here downloads the same two _framework/*.wasm
  // files from the SAME live deployment, and measured locally, 8 concurrent fresh page loads against
  // it intermittently made one of them fail a Subresource Integrity check with a hash matching
  // neither file's real content -- consistent with a burst of concurrent requests for the same asset
  // occasionally being answered 404 at the edge and the browser hashing that error body instead. Not
  // confirmed against the trace, and not something this suite tries to diagnose further; reproducibly
  // gone at 4 concurrent loads, on the same machine, same deployment, same tests, which is what this
  // cap is for. A fixed, moderate number decouples that from whatever core count a future CI runner
  // happens to have; the sibling suite's local static server has no such contention to begin with, so
  // it has no reason to share this cap. If this keeps recurring in CI, it is a `site-smoke` false
  // positive worth its own issue rather than a reason to raise this number back up.
  workers: 4,
  // Same margin as the sibling config, for the same reason: the first navigation to each route pays
  // for the WebAssembly download, and a cold CI runner leaves less headroom than a warm CDN does.
  timeout: 60_000,
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
});
