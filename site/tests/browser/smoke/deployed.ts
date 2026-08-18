import { canonicalSlugs, translatedSlugs } from '../site-pages';
import { expect, test } from '@playwright/test';
import type { APIRequestContext, APIResponse, Page } from '@playwright/test';

/**
 * Every route this suite expects to be live, derived from the content tree rather than written down
 * -- the same reason `site-pages.ts` derives its own list rather than hardcoding it (#46). This
 * suite has no publish output to walk, so it reads the same source `canonicalSlugs`/`translatedSlugs`
 * already read for the sibling suite instead of fetching `sitemap.xml`, which keeps route generation
 * synchronous and lets each route become its own `test()` at module load, exactly as the sibling
 * suite's per-route tests do.
 */
export function deployedRoutes(): string[] {
  const routes = ['/', '/counter/', '/docs/', '/docs/ja/'];
  for (const slug of canonicalSlugs()) {
    routes.push(`/docs/${slug}/`);
  }
  for (const slug of translatedSlugs('ja')) {
    routes.push(`/docs/ja/${slug}/`);
  }
  return routes.sort();
}

/** The one piece of state `installLayoutShiftCollector` and `totalLayoutShift` share with the page. */
type ShiftWindow = typeof window & { __bcfShift: number };

/**
 * Registered before every navigation, so shifts from the very first paint are captured rather than
 * missed by a buffer that only starts once this code runs. The Layout Instability API records a
 * shift the instant it happens, which is what makes it fit for measuring a swap this suite cannot
 * otherwise time: there is no reliable "hydration just finished" event in this app to wait for and
 * then screenshot around (see the note on `settled` below), and unlike a screenshot diff this does
 * not need one either.
 */
export async function installLayoutShiftCollector(page: Page): Promise<void> {
  await page.addInitScript(() => {
    (window as ShiftWindow).__bcfShift = 0;
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as (PerformanceEntry & {
        value: number;
        hadRecentInput: boolean;
      })[]) {
        // A shift the reader caused (e.g. scrolling) is not a regression in this render; the API
        // marks those so they can be excluded rather than counted against the page.
        if (!entry.hadRecentInput) {
          (window as ShiftWindow).__bcfShift += entry.value;
        }
      }
    }).observe({ type: 'layout-shift', buffered: true });
  });
}

/** The Cumulative Layout Shift the collector installed above has recorded so far. */
export function totalLayoutShift(page: Page): Promise<number> {
  return page.evaluate(() => (window as ShiftWindow).__bcfShift);
}

/**
 * Navigate and wait for the client render to settle, returning the navigation response.
 *
 * `networkidle`, not the `document.scripts.length < 2` signal this issue originally proposed: measured
 * against the live deployment, the boot script tag was already gone by the first observable point
 * after `commit`, and `document.scripts.length` never dropped below 2 afterwards either -- the signal
 * does not describe what this deployment actually does. `networkidle` is what the sibling suite's
 * `gotoSettled` already uses across some 300 passing checks, and measured against this same
 * deployment it resolved in about a second.
 */
export async function settled(page: Page, route: string) {
  return page.goto(route, { waitUntil: 'networkidle' });
}

/**
 * A page that threw nothing and logged nothing to the console while it loaded and hydrated.
 *
 * Both listeners must be attached before `goto`, or a failure during the earliest part of the boot
 * -- exactly the part most likely to break -- fires before anything here is listening.
 */
export function collectPageErrors(page: Page): { errors: () => string[] } {
  const errors: string[] = [];
  page.on('pageerror', (err) => errors.push(String(err)));
  page.on('console', (msg) => {
    if (msg.type() === 'error') {
      errors.push(msg.text());
    }
  });
  return { errors: () => errors };
}

/**
 * Exactly one nav link marked active, mirroring the assertion `eng/verify-site-prerender.sh` already
 * makes on the prerendered HTML (`assert_count 'nav-link active' ... 1`). Checking it again here,
 * post-hydration, is the whole point of this file: `SiteNav.CurrentPath()` re-derives the current
 * route from `NavigationManager` on the client independently of the router, so a route the router
 * stops matching would render the client's not-found content while this guard, run only against the
 * prerendered HTML, stayed green.
 */
export async function activeNavLinkCount(page: Page): Promise<number> {
  return page.locator('.nav-link.active').count();
}

/**
 * A GET, retried once after a pause if it did not land on `done`.
 *
 * A reading taken immediately after a deploy can catch the edge mid-propagation and report a stale
 * answer for some paths but not others -- this issue's own words. One retry after a pause is what it
 * asks for, not a poll-until-stable loop, which would hide a genuine, lasting regression behind
 * retries instead of failing on it.
 */
export async function readTwice(
  request: APIRequestContext,
  route: string,
  done: (response: APIResponse) => boolean,
): Promise<APIResponse> {
  const first = await request.get(route, { maxRedirects: 0 });
  if (done(first)) {
    return first;
  }
  await new Promise((resolve) => setTimeout(resolve, 5000));
  return request.get(route, { maxRedirects: 0 });
}

export { expect, test };
