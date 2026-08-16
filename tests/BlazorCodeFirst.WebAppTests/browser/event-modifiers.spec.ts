import { expect, test } from '@playwright/test';

/**
 * What no .NET-side test can reach. The generator emits `__internal_preventDefault_onwheel` as an ordinary
 * attribute, and whether Blazor's client-side event delegator reads it and calls preventDefault() on a real
 * wheel event exists only in a browser. Frames say the attribute is there; they cannot say the page stopped
 * scrolling. bUnit dispatches no wheel event at all.
 *
 * EventModifierParityTests pins the premise this file depends on: the attribute this generator writes is
 * frame-for-frame what the framework's own AddEventPreventDefaultAttribute writes. dotnet test is what
 * checks that, and a red gate there makes a green run here worthless.
 *
 * The unmodified target is not decoration. Without it a page that could not scroll at all would pass every
 * assertion below, and the wheel counter separates "preventDefault stopped the scroll" from "the wheel
 * event never reached Blazor", which is what a passive listener would produce.
 */

test('PreventDefault on an onwheel handler stops the page scrolling underneath it', async ({ page }) => {
  await page.goto('/event-modifiers');

  // Prerendering is off on this page, so the targets existing at all means the circuit connected and the
  // attributes below were written by the interactive renderer rather than by the .NET HtmlRenderer.
  await expect(page.locator('#plain')).toBeVisible();
  await expect(page.locator('#blocked')).toBeVisible();

  // The control: an identical target with no modifier scrolls the page.
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.locator('#plain').hover();
  await page.mouse.wheel(0, 400);
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(0);

  await page.evaluate(() => window.scrollTo(0, 0));
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0);

  // The subject: the modified target does not.
  await page.locator('#blocked').hover();
  await page.mouse.wheel(0, 400);
  await page.waitForTimeout(250);
  expect(await page.evaluate(() => window.scrollY)).toBe(0);

  // And the handler fired, so the non-scroll is preventDefault rather than a lost event.
  await expect.poll(() => page.locator('#wheel-count').textContent()).not.toBe('0');
});

/**
 * The same question asked of a binding's own event, which #370 could not answer from #368's measurement:
 * that one was taken against an EventCallback the author wrote, and a binding's event frame carries a
 * CreateBinder callback instead.
 *
 * stopPropagation and not preventDefault, because `input` is not cancelable — a preventDefault that arrived
 * and was honoured would leave nothing to observe. `input` bubbles, so a parent handler is the observation,
 * and the echo is what separates "the modifier stopped the bubble" from "the binding stopped working".
 */
test("StopPropagation on a binding's own event keeps the input from reaching a parent handler", async ({
  page,
}) => {
  await page.goto('/event-modifiers');

  await expect(page.locator('#plain-input')).toBeVisible();
  await expect(page.locator('#blocked-input')).toBeVisible();

  // The control: an identical binding with no modifier reaches the parent.
  await page.locator('#plain-input').fill('abc');
  await expect.poll(() => page.locator('#plain-echo').textContent()).toBe('abc');
  await expect.poll(() => page.locator('#plain-parent-count').textContent()).not.toBe('0');

  // The subject: the binding still writes its value, and the parent never counts.
  await page.locator('#blocked-input').fill('xyz');
  await expect.poll(() => page.locator('#blocked-echo').textContent()).toBe('xyz');
  await page.waitForTimeout(250);
  expect(await page.locator('#blocked-parent-count').textContent()).toBe('0');
});
