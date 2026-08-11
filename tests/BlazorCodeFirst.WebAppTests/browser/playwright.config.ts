import { defineConfig } from '@playwright/test';

/**
 * The host is started by this config, not separately. `npx playwright test` is the whole local
 * command, and the `browser` job in .github/workflows/ci.yml runs the same one.
 *
 * Three settings here are not defaults and each fixes something that is silently or loudly broken
 * without it.
 *
 * `url` points at /counter rather than at the base URL. This host has no route at "/" — the four
 * routes are /counter, /fold-escaping, /bind-resync, /fold-parity, /null-attribute and
 * /null-attribute-prerender, there is no wwwroot and no
 * <NotFound> — and MapRazorComponents maps one endpoint per @page without a catch-all, so GET /
 * is a 404 (measured, along with /index.html, which is what Playwright retries before giving up).
 * Playwright's readiness probe (isURLAvailable) treats a second 404 as "not ready", so a base-URL
 * probe never succeeds and the run dies on the webServer timeout instead of starting. Do not
 * "simplify" this back to baseURL.
 *
 * `stdout: 'pipe'` because the default discards it. A failed MSBuild, a port already in use, and
 * Kestrel's own "Now listening on" all go to stdout, so without this the only thing a failed CI run
 * shows is "Process from config.webServer was not able to start. Exit code: 1".
 *
 * `--no-launch-profile` because Properties/launchSettings.json carries launchBrowser: true, which
 * opens a real browser window on every local run, plus an applicationUrl that competes with --urls.
 * Ignoring the profile leaves one source for the URL.
 *
 * ASPNETCORE_ENVIRONMENT is then passed explicitly, because dropping the profile also drops the
 * Development it was setting, and the framework does branch on it: WebApplication.CreateBuilder loads
 * the static web assets manifest only in Development, so a Production host that has not been published
 * serves /_framework/blazor.web.js as 200 with a zero-length body. The page loads, no script runs, the
 * circuit never opens, and every test times out waiting for an interactive marker. Measured, not
 * assumed — that is exactly what the first run of this config did.
 *
 * The port is 5100, not 5000: macOS ControlCenter (AirPlay Receiver) listens on 5000, so a host
 * started there fails to bind. That is not a hypothetical — it is why the by-hand instructions this
 * config replaces did not work on macOS at all.
 *
 * Setting BCF_BASE_URL is the one way to point at a host started some other way, and it turns the
 * webServer block off entirely. There is deliberately no reuseExistingServer: reusing whatever
 * happens to hold the port lets a stale build answer the suite and pass, which is the same class of
 * defect as a check that never runs (#163). Without BCF_BASE_URL, an occupied port fails loudly.
 */

const baseURL = process.env.BCF_BASE_URL ?? 'http://localhost:5100';

export default defineConfig({
  testDir: '.',
  retries: 0,
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  webServer: process.env.BCF_BASE_URL
    ? undefined
    : {
        command: `dotnet run --project ../../BlazorCodeFirst.WebAppTestHost --no-launch-profile --urls ${baseURL}`,
        env: { ASPNETCORE_ENVIRONMENT: 'Development' },
        url: `${baseURL}/counter`,
        stdout: 'pipe',
        timeout: 120_000,
      },
});
