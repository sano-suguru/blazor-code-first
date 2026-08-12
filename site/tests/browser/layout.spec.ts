import { WIDTHS, expect, gotoSettled, publishedRoutes, test } from './site-pages';

const ROUTES = publishedRoutes();

/*
 * On overflow, and why this file measures element boxes rather than asking whether the page
 * scrolls.
 *
 * app.css sets `overflow-x: clip` on both html and body, so this site cannot scroll sideways no
 * matter what it contains: `document.documentElement.scrollWidth` is pinned to the viewport and a
 * check written against it passes on every input, including a page 2200 px wide. That was measured
 * rather than reasoned about, by widening a heading in the publish output until it ran a kilometre
 * past the screen and watching the scrollWidth version stay green.
 *
 * Clipping is the right rule for the page (`hidden` would create a scroll container and break the
 * sticky rail, which the comment above those declarations records), but it turns overflow from a
 * visible scrollbar into silently amputated content. So the question here is not whether the page
 * scrolls. It is whether anything was laid out where the reader cannot see it.
 */

/**
 * Elements a reader clicks, whose label has to fit the space laid out for it.
 *
 * Not a cosmetic concern on this site: the header nav gives three destinations and a wordmark
 * 320 px to share, and app.css fits that row by stepping the type down at 40rem rather than by
 * hiding a destination. If that fitting stops working, grep over the publish output cannot tell.
 */
const CLICKABLE = '.site-header a, .site-header button, .site-footer a, .docs-rail a, .site-main button';

for (const width of WIDTHS) {
  test.describe(`at ${width}px`, () => {
    test.use({ viewport: { width, height: 900 } });

    for (const route of ROUTES) {
      test(`${route} keeps every element inside the viewport`, async ({ page }) => {
        await gotoSettled(page, route);

        const outside = await page.evaluate(() => {
          const limit = document.documentElement.clientWidth;

          const offenders: string[] = [];
          for (const el of document.querySelectorAll('body *')) {
            const rect = el.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) {
              continue;
            }
            if (rect.left >= -1 && rect.right <= limit + 1) {
              continue;
            }
            // Content inside a horizontal scroll container is meant to be wider than its box and
            // is reachable by scrolling that container. The prose code blocks are the case: they
            // carry `overflow-x: auto` precisely so a long line stays readable.
            let reachable = false;
            for (let node = el.parentElement; node; node = node.parentElement) {
              const overflowX = getComputedStyle(node).overflowX;
              if (overflowX === 'auto' || overflowX === 'scroll') {
                reachable = true;
                break;
              }
            }
            if (reachable) {
              continue;
            }

            const name = `${el.tagName.toLowerCase()}${typeof el.className === 'string' && el.className ? `.${el.className.trim().split(/\s+/).join('.')}` : ''}`;
            offenders.push(
              `${name} [${Math.round(rect.left)}, ${Math.round(rect.right)}] outside [0, ${limit}]: ${(el.textContent ?? '').trim().slice(0, 40)}`,
            );
          }
          return offenders;
        });

        expect(
          outside,
          'an element extends past the viewport, where html and body being overflow-x: clip means it is cut off rather than scrolled to',
        ).toEqual([]);
      });

      test(`${route} renders every clickable label intact`, async ({ page }) => {
        await gotoSettled(page, route);

        const broken = await page.evaluate((selector) => {
          /*
           * Two failure modes, because the labels on this site split into two groups and each can
           * only fail one way.
           *
           * The header and footer labels compute `white-space: nowrap`, so they never wrap. Given
           * too little room they spill out of their own box instead, over whatever sits beside
           * them, and a check that only counted lines would pass on every one of them forever.
           * The rail links compute `white-space: normal` and do wrap. Both were measured on the
           * published output rather than read off the stylesheet.
           */
          const lineCount = (el: Element) => {
            // Ranges over the element itself return the block's own box in Chromium, not one rect
            // per line. Walking to the text nodes is what makes this count lines.
            const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
            const range = document.createRange();
            const tops = new Set<number>();
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
              if (!(node.textContent ?? '').trim()) {
                continue;
              }
              range.selectNodeContents(node);
              for (const rect of range.getClientRects()) {
                if (rect.width > 0 && rect.height > 0) {
                  tops.add(Math.round(rect.top));
                }
              }
            }
            return tops.size;
          };

          const failures: string[] = [];
          for (const el of document.querySelectorAll(selector)) {
            const rect = el.getBoundingClientRect();
            // The skip link sits off-screen until it takes focus, so it has a box and no reader.
            // Anything with no box at all is equally out of scope here.
            if (rect.width === 0 || rect.height === 0 || rect.right <= 0 || rect.bottom <= 0) {
              continue;
            }
            const label = (el.textContent ?? '').trim();

            const lines = lineCount(el);
            if (lines > 1) {
              failures.push(`${label} wrapped onto ${lines} lines`);
            }
            // clientWidth is 0 on an inline box, where the comparison means nothing.
            if (el.clientWidth > 0 && el.scrollWidth > el.clientWidth + 1) {
              failures.push(`${label} is ${el.scrollWidth}px of text in a ${el.clientWidth}px box`);
            }
          }
          return failures;
        }, CLICKABLE);

        expect(broken, 'a clickable label does not fit the space laid out for it').toEqual([]);
      });
    }

    test('the documentation rail sits beside the document above the breakpoint and below it under', async ({
      page,
    }) => {
      await gotoSettled(page, '/docs/getting-started/');

      const geometry = await page.evaluate(() => {
        const rail = document.querySelector('.docs-rail');
        const content = document.querySelector('.docs-content');
        if (!rail || !content) {
          return null;
        }
        // Read the breakpoint off the root font size rather than assuming 16px, so a change to the
        // root size cannot silently move which arrangement this expects.
        const rootFontSize = parseFloat(getComputedStyle(document.documentElement).fontSize);
        return {
          rail: rail.getBoundingClientRect().toJSON(),
          content: content.getBoundingClientRect().toJSON(),
          breakpoint: 60 * rootFontSize,
          viewport: document.documentElement.clientWidth,
        };
      });

      expect(geometry, 'the documentation shell did not render its rail and its article').not.toBeNull();
      const { rail, content, breakpoint, viewport } = geometry!;

      if (viewport >= breakpoint) {
        // Two columns, rail first. app.css places it with an explicit grid-column at this
        // breakpoint while the DOM order stays article-before-rail, so nothing else in CI would
        // notice that placement being lost.
        expect(
          Math.round(rail.right),
          'the rail is not in the first column beside the document',
        ).toBeLessThanOrEqual(Math.round(content.left));
      } else {
        // One column, article first. The rail is a table of contents, and a reader who asked for a
        // document should not have to scroll past the list of the others to reach it.
        expect(
          Math.round(rail.top),
          'the rail is not below the document it belongs to',
        ).toBeGreaterThanOrEqual(Math.round(content.bottom));
      }
    });
  });
}

test.describe('the Japanese edition', () => {
  // <html lang> is baked into index.html as "en" and is not touched: the shell stays English while
  // the documents do not. Marking the article is therefore both the accurate claim and what WCAG
  // 3.1.2 (Language of Parts) asks for, and it is what a screen reader needs to switch voices.
  // Documents only. The index is a section rather than an article, and is checked separately below.
  const japanese = ROUTES.filter((r) => r.startsWith('/docs/ja/') && r !== '/docs/ja/');

  test('there is a Japanese document to check', () => {
    expect(japanese.length, 'no /docs/ja/<slug> route was published, so the checks below prove nothing')
      .toBeGreaterThan(0);
  });

  for (const route of japanese) {
    test(`${route} declares its language on the article`, async ({ page }) => {
      await gotoSettled(page, route);
      await expect(page.locator('article.docs-content')).toHaveAttribute('lang', 'ja');
      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    });
  }

  test('/docs/ja declares its language on the index body', async ({ page }) => {
    await gotoSettled(page, '/docs/ja');
    await expect(page.locator('section.docs-content')).toHaveAttribute('lang', 'ja');
  });

  test('/docs/ja and /docs link to each other', async ({ page }) => {
    await gotoSettled(page, '/docs/ja');
    await expect(page.locator('.lang-switch a[lang="en"]')).toHaveAttribute('href', '/docs');

    await gotoSettled(page, '/docs');
    await expect(page.locator('.lang-switch a[lang="ja"]')).toHaveAttribute('href', '/docs/ja');
  });

  test('a document with no counterpart offers no switch', async ({ page }) => {
    // control-flow is English-only, and a switch there would link to a route that was never
    // generated: a 404 reached by following the site's own navigation.
    await gotoSettled(page, '/docs/control-flow');
    await expect(page.locator('.lang-switch')).toHaveCount(0);
  });
});

test.describe('at 375px', () => {
  test.use({ viewport: { width: 375, height: 900 } });

  test('an unknown path is served the not-found page inside the shell', async ({ page }) => {
    // Not a route in the published output, so it exercises serve.mjs's 404 fallback and the shared
    // [ViewPart] body that 404.html and DocsPage's unknown-slug branch both render.
    const response = await page.goto('/no-such-page', { waitUntil: 'networkidle' });
    expect(response?.status()).toBe(404);
    await expect(page.locator('.site-nav')).toBeVisible();
    await expect(page.locator('h1')).toHaveText('Page not found');
  });
});
