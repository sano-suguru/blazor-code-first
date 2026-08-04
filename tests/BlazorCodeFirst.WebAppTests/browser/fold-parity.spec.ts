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
 */
const cases = [
  'table-fragment',
  'select-options',
  'escaped-text',
  'quoted-attribute',
  'void-in-run',
  'multi-class',
];

test.beforeEach(async ({ page }) => {
  await page.goto('/fold-parity');

  // Wait for the interactive render, so the comparison is of live DOM and not of the prerendered HTML
  // that the .NET HtmlRenderer already writes verbatim (PrerenderTests covers that path).
  await page.waitForSelector('#interactive-marker');
});

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
// other; these additionally pin what that (shared) DOM must actually contain, so a defect that escapes
// neither path identically cannot pass by agreeing with itself.

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
