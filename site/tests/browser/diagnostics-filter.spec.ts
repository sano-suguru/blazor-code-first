import { expect, gotoSettled, test } from './site-pages';

/**
 * The diagnostics filter's live region (#418): AnchorFilter replaces the chip list with no other
 * announcement, so a screen reader has no way to learn that typing narrowed it without this.
 *
 * Both editions run the same body: the wording differs (`shell.yml` puts the total first in
 * Japanese), but the two counts appearing in the announcement, and it changing when the chip list
 * does, are the properties that matter in either language.
 */
const ROUTES = ['/docs/diagnostics/', '/docs/ja/diagnostics/'];

for (const route of ROUTES) {
  test.describe(route, () => {
    test('the live region is polite and hidden from sighted readers', async ({ page }) => {
      await gotoSettled(page, route);

      const status = page.locator('.anchor-filter [role="status"]');
      await expect(status).toHaveAttribute('aria-live', 'polite');
      await expect(status).toHaveClass(/visually-hidden/);
    });

    test('announces the full count before the reader types anything', async ({ page }) => {
      await gotoSettled(page, route);

      // Before WebAssembly starts every chip is shown (AnchorFilter's own doc comment), and that
      // resting state is what the prerendered HTML must already carry.
      const totalCount = await page.locator('.anchor-chip').count();
      const status = page.locator('.anchor-filter [role="status"]');
      await expect(status).toContainText(String(totalCount));
    });

    test('narrows the announced count as the reader types', async ({ page }) => {
      await gotoSettled(page, route);

      const status = page.locator('.anchor-filter [role="status"]');
      const restingText = await status.innerText();
      const totalCount = await page.locator('.anchor-chip').count();

      // bcf3016 is a real id every edition documents (AnchorFilterTests.IdsAreOfferedInReadingOrder
      // exercises it against the same manifest), so this narrows without depending on document
      // order or count.
      await page.fill('#anchor-filter', '3016');

      const narrowedCount = await page.locator('.anchor-chip').count();
      expect(
        narrowedCount,
        'the filter itself must have narrowed for this test to prove anything',
      ).toBeLessThan(totalCount);

      await expect(status).not.toHaveText(restingText);
      await expect(status).toContainText(String(narrowedCount));
    });
  });
}
