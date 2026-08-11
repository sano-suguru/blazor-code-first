import { readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { test as base, expect } from '@playwright/test';
import type { Page, Route } from '@playwright/test';

/** The `dotnet publish` output this suite measures. serve.mjs is pointed at the same directory. */
export const publishRoot = resolve(
  __dirname,
  '../../BlazorCodeFirst.Site/bin/Release/net10.0/publish/wwwroot',
);

/**
 * The viewport widths every route is measured at. 320 is the narrowest phone still worth
 * supporting, 375 and 414 are the two common iPhone classes, 768 is a portrait tablet, and 1280 and
 * 1920 are the two laptop and desktop widths. The set straddles the 60rem layout breakpoint in
 * css/app.css with four widths below it and two above, which is what makes the rail assertion in
 * layout.spec.ts able to check both arrangements from one list.
 */
export const WIDTHS = [320, 375, 414, 768, 1280, 1920] as const;

/**
 * Every prerendered route, read out of the publish output rather than written down.
 *
 * site.yml's shell assertions hardcode four document slugs and the output currently carries six, a
 * drift #46 tracks. Deriving the list here means a new document is measured from the commit that
 * adds it, and a route that stops being prerendered fails as a missing page instead of quietly
 * dropping out of the run.
 *
 * 404.html is deliberately absent: it is not a directory index, so it is not reachable by walking
 * for index.html, and serve.mjs returns it for any unknown path. layout.spec.ts requests one such
 * path explicitly.
 */
export function publishedRoutes(): string[] {
  const routes: string[] = [];

  const walk = (dir: string, prefix: string) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      // _framework holds the WebAssembly payload and no routes. Skipping it keeps this walk off
      // several thousand files.
      if (entry.isDirectory() && !entry.name.startsWith('_')) {
        walk(join(dir, entry.name), `${prefix}${entry.name}/`);
      } else if (entry.name === 'index.html') {
        routes.push(prefix);
      }
    }
  };

  walk(publishRoot, '/');
  return routes.sort();
}

/**
 * One fetch per worker for each Google Fonts URL the site asks for, replayed to every page after
 * that.
 *
 * index.html loads Geist and JetBrains Mono from fonts.googleapis.com, so every page here would
 * otherwise reach the network, and every width measured is a question about how much room text
 * takes. Without the cache the first run of this suite lost the mono face on 15 of 133 tests: the
 * requests are identical, they arrive in parallel from every worker, and some of them come back
 * empty. A missing face is not a slow page, it is a different measurement, so this cannot be left
 * to chance.
 *
 * Caching keeps the fonts the real ones rather than substituting a local stand-in, which a
 * measurement of text size has to. It does not remove the network from the run: the first request
 * per worker is real, and an unreachable fonts.googleapis.com still fails the suite. #252 tracks
 * removing the dependency at its source by self-hosting the two families.
 */
const fontCache = new Map<string, { body: Buffer; contentType: string }>();

async function replayFromCache(route: Route): Promise<void> {
  const url = route.request().url();
  let hit = fontCache.get(url);

  if (!hit) {
    // The stylesheet response is negotiated on User-Agent: Google returns woff2 `src` URLs to a
    // browser and older formats to anything it does not recognise. Forwarding the browser's own
    // headers is what keeps the cached copy the one the page would have received.
    const response = await fetch(url, { headers: route.request().headers() });
    if (!response.ok) {
      await route.abort();
      return;
    }
    hit = {
      body: Buffer.from(await response.arrayBuffer()),
      contentType: response.headers.get('content-type') ?? 'application/octet-stream',
    };
    fontCache.set(url, hit);
  }

  await route.fulfill({ body: hit.body, contentType: hit.contentType });
}

/**
 * The suite's `test`, with the font cache installed on every page before it navigates anywhere.
 * Specs import this rather than `@playwright/test`'s own.
 */
export const test = base.extend<{ cachedFonts: void }>({
  cachedFonts: [
    async ({ page }, use) => {
      await page.route('https://fonts.googleapis.com/**', replayFromCache);
      await page.route('https://fonts.gstatic.com/**', replayFromCache);
      await use();
    },
    { auto: true },
  ],
});

export { expect };

/**
 * Load a route and wait until what is on screen is what a reader would settle on.
 *
 * Two waits, and neither is optional. `networkidle` covers the WebAssembly boot: every route is
 * prerendered, so the markup is on screen before any script runs, but Blazor then replaces `#app`
 * with its own render of the same page, and measuring across that swap would compare two different
 * documents. Locally the boot completes in about half a second.
 *
 * The fonts are then asserted rather than awaited on trust. Measured against the fallback stack, a
 * label that wraps in production fits here and the check passes on a page nobody will see, so a
 * face that failed to arrive has to fail the test rather than quietly change what it measured.
 *
 * What is asserted is every face THIS page asks for, collected from the computed styles of the
 * elements that draw text, rather than a written-down list of families and weights. A fixed list
 * gets this wrong in both directions. The browser downloads a face only once something on the page
 * needs it, so `/docs/` has no mono 400 on it at all and a list naming that face fails there for a
 * reason that is not a defect; and a list cannot know about a weight a future page introduces.
 */
export async function gotoSettled(page: Page, route: string): Promise<void> {
  await page.goto(route, { waitUntil: 'networkidle' });

  // Requesting each face rather than waiting for `document.fonts.ready` alone. That promise
  // resolves for the loads in flight when it is read, and a face the layout has not yet demanded
  // has not started loading, so reading it once reported a missing face on 1 test in 133. Asking
  // for the faces first turns the wait into one with a defined end.
  const unavailable = await page.evaluate(async () => {
    const wanted = new Set<string>();
    for (const el of document.querySelectorAll('*')) {
      const drawsText = [...el.childNodes].some(
        (n) => n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim(),
      );
      if (!drawsText) {
        continue;
      }
      const style = getComputedStyle(el);
      // The first entry only. The rest of the stack is the fallback, which is by definition
      // available, and asking about it would report every system font as a miss.
      const family = style.fontFamily.split(',')[0].trim();
      wanted.add(`${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${family}`);
    }

    // A rejection here means the face could not be fetched, which `check` reports on its own a
    // line later. Swallowing it keeps that one report rather than producing two.
    await Promise.all([...wanted].map((face) => document.fonts.load(face).catch(() => undefined)));
    await document.fonts.ready;

    return [...wanted].filter((face) => !document.fonts.check(face));
  });

  expect(
    unavailable,
    'a font face this page renders text in is not available, so every measurement below would describe the fallback stack instead',
  ).toEqual([]);
}
