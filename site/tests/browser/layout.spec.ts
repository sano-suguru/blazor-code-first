import {
  WIDTHS,
  canonicalSlugs,
  expect,
  gotoSettled,
  publishedRoutes,
  test,
  translatedSlugs,
} from './site-pages';

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
      /*
       * Both measurements from one navigation, and soft assertions so that costs nothing.
       *
       * These were two tests. Each called `gotoSettled` on the same route at the same viewport, so
       * across six widths and sixteen routes the suite paid for 96 page loads that rendered a
       * document it had already rendered -- and a load here is not cheap, since `gotoSettled` waits
       * out the WebAssembly boot and then the font faces. That was a third of the suite's runtime
       * spent re-answering `goto`.
       *
       * What the split did buy is a page that fails both ways reporting both, rather than stopping
       * at the first `expect`. `expect.soft` keeps exactly that, so the merge gives up nothing but
       * the two names in the report.
       */
      test(`${route} fits the viewport and renders every clickable label intact`, async ({ page }) => {
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

        expect.soft(
          outside,
          'an element extends past the viewport, where html and body being overflow-x: clip means it is cut off rather than scrolled to',
        ).toEqual([]);

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
          /*
           * Whether a reader can see this text at all.
           *
           * A clickable can carry text that is deliberately not on screen, and counting it reports
           * a second line nobody can see. Two idioms produce it, both in the theme control: its
           * accessible name sits in a .visually-hidden span, and the two state words that are not
           * current are `visibility: hidden` so that they keep contributing their width and the
           * button does not change size as it cycles. Each sits at its own top.
           *
           * Neither condition can hide a real wrap. A label a reader can see is `visibility:
           * visible` and is not inside the clip utility, so every rect this used to count for a
           * visible label it still counts. What is dropped is text that has no reader.
           *
           * `visibility` is checked on the parent element alone because it inherits, so a hidden
           * ancestor has already computed onto the text's own parent.
           */
          const seen = (node: Node, within: Element) => {
            const parent = node.parentElement;
            if (!parent || getComputedStyle(parent).visibility === 'hidden') {
              return false;
            }
            for (let a: Element | null = parent; a; a = a.parentElement) {
              if (a.classList.contains('visually-hidden')) {
                return false;
              }
              if (a === within) {
                break;
              }
            }
            return true;
          };

          const lineCount = (el: Element) => {
            // Ranges over the element itself return the block's own box in Chromium, not one rect
            // per line. Walking to the text nodes is what makes this count lines.
            const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
            const range = document.createRange();
            const tops = new Set<number>();
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
              if (!(node.textContent ?? '').trim() || !seen(node, el)) {
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

        expect.soft(broken, 'a clickable label does not fit the space laid out for it').toEqual([]);
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
    const slug = route.slice('/docs/ja/'.length).replace(/\/$/, '');

    test(`${route} declares its language on the article and links home to its original`, async ({ page }) => {
      await gotoSettled(page, route);
      await expect(page.locator('article.docs-content')).toHaveAttribute('lang', 'ja');
      await expect(page.locator('html')).toHaveAttribute('lang', 'en');

      /*
       * The switch in the other direction, which nothing else in this file reaches. `Docs.Href`
       * spells it as `RoutePrefix(lang) + "/" + slug`, and `RoutePrefix("en")` is the `_` arm that
       * carries no language segment at all; the English-to-Japanese assertions below only ever
       * reach the "ja" arm through `Href`, and `/docs/ja and /docs link to each other` reaches
       * `RoutePrefix` with no slug. So a wrong prefix or a dropped slug on this side would ship
       * green while every Japanese document pointed somewhere useless.
       *
       * Unconditional, unlike the two branches below: a translation is a translation OF something,
       * so a document reachable at /docs/ja/<slug>/ has an original at /docs/<slug>.
       */
      await expect(page.locator('.lang-switch a[lang="en"]')).toHaveAttribute('href', `/docs/${slug}`);
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

  // A switch is offered exactly when the document has a Japanese source. Naming one English-only
  // document here instead would assert that until the document is translated, and then assert
  // nothing: the test would keep passing against a page that now has a counterpart, having silently
  // stopped covering the case it was written for.
  //
  // Both sides read the content tree rather than ROUTES, and neither is arbitrary; see the comments
  // on the two helpers. The switch decides what the prerenderer discovers, so a route-derived
  // expectation agrees with a defect instead of catching it, and a route on its own cannot say
  // whether `/docs/<x>/` is a document or a translation index.
  const canonical = canonicalSlugs();
  const translated = translatedSlugs('ja');
  const english = ROUTES.filter((r) => r.startsWith('/docs/'))
    .map((route) => ({ route, slug: route.slice('/docs/'.length).replace(/\/$/, '') }))
    .filter(({ slug }) => canonical.has(slug));

  test('there is an English document to check', () => {
    expect(english.length, 'no /docs/<slug> route matched a content/<slug>.md, so the checks below prove nothing')
      .toBeGreaterThan(0);
  });

  for (const { route, slug } of english) {
    const hasCounterpart = translated.has(slug);
    // Named in full on both branches. Sharing a prefix across the two reads as the opposite of what
    // the second one asserts: "offers a switch to nothing" for a page that offers no switch at all.
    const title = hasCounterpart
      ? `${route} offers a switch to its counterpart`
      : `${route} offers no switch`;

    test(title, async ({ page }) => {
      await gotoSettled(page, route);

      if (!hasCounterpart) {
        // A switch here would link to a document that was never written: a page in the wrong
        // language, or a 404, reached by following the site's own navigation.
        //
        // Dormant as this was written, because every document is translated. Deriving the branch is
        // what lets it wake up on the commit that adds an untranslated document rather than needing
        // one named here, but until then it proves nothing; #279 covers moving the case somewhere it
        // can be constructed.
        await expect(page.locator('.lang-switch')).toHaveCount(0);
        return;
      }

      await expect(page.locator('.lang-switch a[lang="ja"]')).toHaveAttribute('href', `/docs/ja/${slug}`);
    });
  }
});

test.describe('a prose table', () => {
  // The narrowest supported width, where the prose column is tightest and a table has the least
  // room to fit in.
  test.use({ viewport: { width: 320, height: 900 } });

  const route = '/docs/components-and-reuse/';

  // One navigation for both measurements, and soft assertions so that costs nothing -- the same
  // trade the per-route test above records at length.
  test(`${route} publishes a table and keeps a cell too wide for the column inside it`, async ({ page }) => {
    await gotoSettled(page, route);

    // The pipeline reaching the page, measured on the published document rather than on the
    // converter. A '|'-delimited table is a paragraph of literal pipes without Markdig's pipe-table
    // extension, and the extension being dropped from MarkdownConverter's pipeline would show up
    // here as no table at all -- which is the state #333 found, with css/app.css already carrying a
    // `.prose table` rule for a construct nothing could emit.
    expect.soft(await page.locator('.prose table').count(), 'the document published no table').toBe(1);

    /*
     * Why the cell is widened here rather than written into the document.
     *
     * `.prose table` is `display: block; overflow-x: auto`, so the table is its own horizontal
     * scroll container. The table this page carries never uses it: measured at all six widths, its
     * scrollWidth equals its clientWidth, because every cell wraps -- `.prose code` is
     * `overflow-wrap: anywhere`, so even the code spans break rather than push the column open. So
     * an assertion over the document as authored would pass with the rule deleted and prove
     * nothing.
     *
     * What the rule exists for is the cell that cannot shrink, and that condition is created here.
     * A document written to have one would be a document contorted to suit a test.
     */
    const measured = await page.evaluate(() => {
      const table = document.querySelector('.prose table') as HTMLElement | null;
      const cell = table?.querySelector('td') as HTMLElement | null;
      if (!table || !cell) {
        return null;
      }
      cell.textContent = 'x'.repeat(200);
      cell.style.whiteSpace = 'pre';

      return {
        scrollWidth: table.scrollWidth,
        clientWidth: table.clientWidth,
        right: Math.round(table.getBoundingClientRect().right),
        viewport: document.documentElement.clientWidth,
      };
    });

    expect(measured, 'the document rendered no table to measure').not.toBeNull();
    const { scrollWidth, clientWidth, right, viewport } = measured!;

    expect.soft(
      scrollWidth,
      'the table did not become wider than its own box, so this measures nothing about the scroll container',
    ).toBeGreaterThan(clientWidth);

    // html and body are `overflow-x: clip` on this site, so a table that grew the page instead of
    // scrolling inside itself would have its far side cut off rather than scrolled to. See the
    // comment at the top of this file.
    expect.soft(right, 'the overflowing table pushed the page past the viewport').toBeLessThanOrEqual(viewport + 1);
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
