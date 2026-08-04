import { expect, test } from '@playwright/test';

/**
 * The only check that exercises Blazor's browser-side markup path. A text frame reaches the DOM through
 * createTextNode, while a markup frame is parsed by assigning innerHTML on a shared <template> element
 * and moving the resulting nodes. bUnit parses a document string with AngleSharp and prerendering writes
 * markup verbatim, so neither can see a difference between the two paths.
 *
 * Each case name matches a pair of #folded-<name>/#unfolded-<name> containers rendered by
 * FoldParityView (see BlazorCodeFirst.WebAppTestHost). FoldParityTests in WebAppTests pins, on the .NET
 * side, that the folded container really collapses to one AddMarkupContent frame and the unfolded one
 * really does not; that is the premise this comparison depends on, and dotnet test is what checks it.
 *
 * A second premise belongs to this file specifically: prerendering must not have already written this
 * content into the initial HTML response. FoldParityPage.razor disables prerendering for this one route
 * (see the remarks on FoldParityView.cs) precisely so that claim is true. The first test below verifies
 * it directly, with no `beforeEach` navigation applied to it, by fetching the same URL with no browser
 * involved and checking the containers are absent from that response. Without that check, this whole
 * file could pass while comparing two subtrees the .NET HtmlRenderer wrote into the very first
 * response — the same server-written markup twice, and the path PrerenderTests already covers, not the
 * browser-side insertMarkup/insertText path this file exists to check.
 */

test('prerendering is off, so the containers are only ever populated by the interactive circuit’s first render', async ({
  page,
  request,
}) => {
  // The same URL, fetched directly with no browser involved: this is exactly the HTML
  // WebApplicationFactory-based PrerenderTests would see, and exactly what a browser's initial
  // navigation receives before any script runs.
  //
  // Asserting the container id is ABSENT here is the point, not an oversight to tidy up: an empty
  // prerender response is what forces this content through the client-side renderer in the first
  // place, since there is nothing else that could ever put it in the DOM. Every DOM comparison in the
  // describe block below depends on that — if prerendering ever starts writing this content again
  // (someone reverts FoldParityPage's render mode, or the framework's behavior shifts), those
  // comparisons would keep passing while silently going back to comparing two subtrees the .NET
  // HtmlRenderer wrote verbatim, the same server-written markup twice. This assertion is what catches
  // that regression, so it must fail loudly rather than being deleted as a "test that never renders".
  const prerendered = await (await request.get('/fold-parity')).text();
  expect(prerendered).not.toContain('folded-table-fragment');
  expect(prerendered).not.toContain('unfolded-table-fragment');

  // Now load it in the browser. The container must be absent immediately after navigation (the same
  // HTML just fetched above) and present only once the interactive circuit's first render batch runs.
  // If it appeared only after this point, it was built by the client-side renderer
  // (createElement/setAttribute/createTextNode or insertMarkup), not copied verbatim from server HTML.
  await page.goto('/fold-parity');
  await expect(page.locator('#folded-table-fragment')).toHaveCount(0);

  await page.waitForSelector('#interactive-marker');
  await expect(page.locator('#folded-table-fragment')).toHaveCount(1);
});

test.describe('folded and unfolded spellings, once the interactive circuit has rendered', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/fold-parity');

    // Wait for the interactive render, so the comparison is of live DOM and not of the prerendered
    // HTML that the .NET HtmlRenderer already writes verbatim (PrerenderTests covers that path). The
    // premise that this page has no prerendered content to already agree with itself is the test above.
    await page.waitForSelector('#interactive-marker');
  });

  const cases = [
    'table-fragment',
    'select-options',
    'escaped-text',
    'quoted-attribute',
    'void-in-run',
    'multi-class',
  ];

  for (const name of cases) {
    test(`folded and unfolded spellings render the same DOM: ${name}`, async ({ page }) => {
      const folded = await page.locator(`#folded-${name}`).innerHTML();
      const unfolded = await page.locator(`#unfolded-${name}`).innerHTML();

      expect(folded).toBe(unfolded);
    });
  }

  // The escaped-text and quoted-attribute cases carry payloads chosen specifically to fail loudly if
  // escaping is missing: a raw closing script tag, a raw HTML comment, an img/onerror string, and an
  // attribute-breakout string. The loop above already proves the folded and unfolded DOM match each
  // other; these additionally pin what that (shared) DOM must actually contain, so a defect that
  // escapes neither path identically cannot pass by agreeing with itself.

  test('escaped text stays text and is not interpreted as markup', async ({ page }) => {
    const folded = page.locator('#folded-escaped-text');

    await expect(folded.locator('p').nth(0)).toHaveText('a & b < c > d');
    await expect(folded.locator('p').nth(1)).toHaveText('</script>');
    await expect(folded.locator('p').nth(2)).toHaveText('<!-- x -->');
    await expect(folded.locator('p').nth(3)).toHaveText('<img src=x onerror=alert(1)>');

    // If the img payload were parsed as an element instead of escaped text, it would be a real <img> node.
    await expect(folded.locator('img')).toHaveCount(0);
  });

  test('quoted and breakout attribute values stay single attribute values', async ({ page }) => {
    const folded = page.locator('#folded-quoted-attribute');

    await expect(folded.locator('span').nth(0)).toHaveAttribute('data-value', 'say "hi"');
    await expect(folded.locator('span').nth(1)).toHaveAttribute(
      'data-value',
      '" onmouseover="alert(1)',
    );

    // If the breakout payload had closed the attribute, onmouseover would show up as its own attribute
    // rather than as part of data-value's value.
    await expect(folded.locator('span').nth(1)).not.toHaveAttribute('onmouseover', /.*/);
  });

  test('a void tag inside a folded run has no closing tag or children', async ({ page }) => {
    const folded = page.locator('#folded-void-in-run');

    await expect(folded.locator('img')).toHaveAttribute('src', 'pixel.gif');
    await expect(folded.locator('img')).toHaveAttribute('alt', 'px');
    expect(await folded.locator('img').innerHTML()).toBe('');
  });

  test('multiple .Class(...) decorations join into one space-separated class attribute', async ({
    page,
  }) => {
    const folded = page.locator('#folded-multi-class');

    await expect(folded.locator('span')).toHaveClass('btn btn-primary wide');
  });
});
