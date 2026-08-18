import { defineConfig } from '@playwright/test';
import { publishRoot } from './site-pages';

/**
 * Browser checks for the documentation site, run against the `dotnet publish` output.
 *
 * Everything site.yml asserts today it asserts with grep, which can prove the shell rendered and
 * the stylesheet links survived and cannot see anything the browser computes: overflow, whether a
 * label wrapped, what contrast a colour pair produces, where the documentation rail landed. This
 * suite is that half. It runs in `build-deploy` after the publish and before the deploy, so a
 * failure stops the deployment rather than describing it afterwards (#251).
 *
 * It measures a local publish output on purpose, not a deployment. Hydration behaviour and
 * Cloudflare's edge routing genuinely need one and are #47's subject, covered by `smoke/` under its
 * own config; layout, wrapping, and colour need nothing but the files and a static server, and tying
 * them to a deployment would put them behind #66.
 *
 * The port is 5200. 5100 belongs to tests/BlazorCodeFirst.WebAppTests/browser, and 5000 is held by
 * macOS ControlCenter.
 *
 * `stdout: 'pipe'` because the default discards it, and a server that failed to start would
 * otherwise show up only as Playwright's generic webServer timeout.
 *
 * There is deliberately no `reuseExistingServer`. Reusing whatever holds the port lets a stale
 * publish answer the suite and pass, which is the same class of defect as a check that never runs
 * (#163). Setting BCF_SITE_BASE_URL is the one supported way to point this at a server started
 * some other way, and it turns the webServer block off entirely.
 */

const baseURL = process.env.BCF_SITE_BASE_URL ?? 'http://localhost:5200';

export default defineConfig({
  testDir: '.',
  // `smoke/` measures a deployment under its own config (playwright.smoke.config.ts), which requires
  // BCF_SITE_BASE_URL and refuses to fall back to this suite's local server. Without this exclusion,
  // testDir: '.' would also collect it here, where the local server answers every route with content
  // that was never deployed and the smoke assertions would fail for a reason that is not a defect.
  testIgnore: '**/smoke/**',
  retries: 0,
  // Without this Playwright parallelises by FILE, and this suite is two files: it ran its couple of
  // hundred tests on two workers and ignored `--workers` entirely, on a four-core CI runner. Every
  // test here opens its own page and shares nothing with its neighbours -- the routes are read off
  // the publish output and the server is read-only -- so file order was never carrying anything.
  fullyParallel: true,
  // Paired with the line above, because the default is half the logical cores and half of a
  // four-core runner is the same 2 that the file-level cap already imposed.
  workers: '100%',
  // Every test navigates, and the first navigation of a run also pays for the WebAssembly download.
  // The default 30s is enough locally and leaves no margin on a cold CI runner. The fonts used to be
  // in that first navigation too, over the network; they are served from the publish output now
  // (#252), so whether this can come down is a measurement nobody has made.
  timeout: 60_000,
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  webServer: process.env.BCF_SITE_BASE_URL
    ? undefined
    : {
        command: `node serve.mjs ${publishRoot} 5200`,
        url: `${baseURL}/`,
        stdout: 'pipe',
        timeout: 30_000,
      },
});
