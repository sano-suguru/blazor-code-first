import { expect, test } from '@playwright/test';

/**
 * What no .NET-side test can reach. When a value turns null the diff asks for RemoveAttribute, and whether
 * the client-side renderer applies it — and patches the element already in the DOM instead of replacing it
 * — exists only in a browser. bUnit has no client-side renderer at all, and prerendering writes a single
 * static response with no second render to diff against.
 *
 * NullAttributeFrameTests pins the premise this file depends on: a null attribute value appends no frame,
 * so an absent attribute in the DOM means the value was omitted rather than written as something else.
 * dotnet test is what checks that, and a red gate there makes a green run here worthless.
 */

test('prerendering is off, so these attributes are only ever written by the interactive circuit', async ({
  request,
}) => {
  // Asserting the ids are ABSENT is the point, not an oversight. With prerendering on, the first HTML
  // response would already carry the final attributes; a first render batch identical to that DOM produces
  // no edits, the client-side renderer never runs, and every assertion below would be reading markup the
  // .NET HtmlRenderer wrote — which is what PrerenderTests covers, over the sibling route that leaves
  // prerendering on. This assertion is what catches that regression, so it must fail loudly rather than be
  // deleted as a test that never renders anything.
  const prerendered = await (await request.get('/null-attribute')).text();
  expect(prerendered).not.toContain('id="to-null"');
});

test.describe('a null attribute value', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/null-attribute');
    await expect(page.locator('#state')).toHaveText('present');
  });

  test('is written on the first interactive render, where the empty string is a different value', async ({
    page,
  }) => {
    await expect(page.locator('#to-null')).toHaveAttribute('title', 'tip');
    await expect(page.locator('#to-empty')).toHaveAttribute('title', 'tip');
    // A true bool reaches the DOM as an empty value here, while the prerendered HTML writes it bare; both
    // parse to the same DOM, which is what lets the fold write name="" for it.
    await expect(page.locator('#bool')).toHaveAttribute('data-v', '');
  });

  test('is removed from the DOM when the value turns null, while an empty string stays', async ({
    page,
  }) => {
    await page.locator('#toggle').click();
    await expect(page.locator('#state')).toHaveText('absent');

    await expect(page.locator('#to-null')).not.toHaveAttribute('title', /.*/);
    await expect(page.locator('#to-empty')).toHaveAttribute('title', '');
    await expect(page.locator('#bool')).not.toHaveAttribute('data-v', /.*/);
  });

  test('leaves the separator behind in a joined class and omits a lone one', async ({ page }) => {
    await page.locator('#toggle').click();
    await expect(page.locator('#state')).toHaveText('absent');

    // The exact string, not classList: classList reads "card " as ["card"] and cannot see the residue this
    // pins (#236). The residue is not new — the spelling this replaced, .Class(on ? "on" : ""), produced
    // it too — and normalizing it is deferred, so what a change to it would mean is recorded here.
    await expect(page.locator('#class-join')).toHaveAttribute('class', 'card ');
    await expect(page.locator('#class-single')).not.toHaveAttribute('class', /.*/);
  });

  test('patches the element already in the DOM rather than replacing it', async ({ page }) => {
    // Why this is worth a test: the spelling #171 replaces lifted the whole element into a conditional,
    // which gives the two branches disjoint sequence spaces and so destroys and recreates the element. A
    // property set from script survives a patch and cannot survive a replacement, so this is what makes
    // "the attribute moved, the element did not" a measured claim rather than an inference from the diff.
    await page.evaluate(() => {
      const element = document.getElementById('to-null');
      if (element === null) {
        throw new Error('#to-null is missing before the toggle');
      }

      (element as unknown as { __probe?: boolean }).__probe = true;
    });

    await page.locator('#toggle').click();
    await expect(page.locator('#state')).toHaveText('absent');

    const survived = await page.evaluate(
      () =>
        (document.getElementById('to-null') as unknown as { __probe?: boolean })?.__probe === true,
    );
    expect(survived).toBe(true);
  });
});
