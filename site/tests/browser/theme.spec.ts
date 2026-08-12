import { expect, gotoSettled, test } from './site-pages';
import type { Page } from '@playwright/test';

/**
 * The reader's colour-scheme choice: the attribute it is held in, the cycle that changes it, and
 * the two properties that made it worth building this way rather than the obvious way.
 *
 * Everything here reads `<html data-theme>` rather than a colour, because that attribute is the
 * whole of the runtime state -- css/tokens.css turns it into a palette and nothing else holds a
 * copy. The one test that does measure a colour is the one asserting the choice actually beats the
 * operating system, which is the thing a colour can answer and an attribute cannot.
 */

const TOGGLE = '[data-theme-toggle]';

/** The attribute, or null when it is absent -- which is the "follow the system" state. */
function storedTheme(page: Page): Promise<string | null> {
  return page.evaluate(() => document.documentElement.getAttribute('data-theme'));
}

function persistedTheme(page: Page): Promise<string | null> {
  return page.evaluate(() => localStorage.getItem('theme'));
}

/** The one state word the button is showing. The other two are in the DOM and hidden. */
async function visibleState(page: Page): Promise<string> {
  return (await page.locator(`${TOGGLE} .theme-toggle__states > span:visible`).innerText()).trim();
}

test('a reader who has chosen nothing follows the system', async ({ page }) => {
  await gotoSettled(page, '/');

  expect(await storedTheme(page), 'a fresh reader must carry no override').toBeNull();
  expect(await persistedTheme(page)).toBeNull();
  expect(await visibleState(page)).toBe('System');
});

test('the control cycles system, light, dark, and back', async ({ page }) => {
  await gotoSettled(page, '/');

  // Asserted as a cycle rather than as three separate clicks: the property is that it returns, and
  // a fourth state or a dead end would only show on the wrap.
  for (const expected of ['light', 'dark', null, 'light']) {
    await page.click(TOGGLE);

    expect(await storedTheme(page)).toBe(expected);
    // localStorage tracks the attribute exactly, including being cleared rather than set to a
    // "system" string -- absence is what the next page load reads as "no override".
    expect(await persistedTheme(page)).toBe(expected);
  }
});

test('the visible word names the state', async ({ page }) => {
  await gotoSettled(page, '/');

  await page.click(TOGGLE);
  expect(await visibleState(page)).toBe('Light');

  await page.click(TOGGLE);
  expect(await visibleState(page)).toBe('Dark');
});

test('the choice survives a reload', async ({ page }) => {
  await gotoSettled(page, '/');
  await page.click(TOGGLE);
  await page.click(TOGGLE);
  expect(await storedTheme(page)).toBe('dark');

  await gotoSettled(page, '/docs/getting-started/');

  // A different route, so this also covers the choice being applied by something every page has
  // rather than by anything the landing page carries on its own.
  expect(await storedTheme(page), 'the stored choice was not applied on the next page').toBe('dark');
  expect(await visibleState(page)).toBe('Dark');
});

/**
 * The state the media query could never express, and the reason the palette was rewritten around
 * light-dark() instead of gaining a second block.
 *
 * Measures the surface rather than the attribute: `prefers-color-scheme: dark` is emulated for the
 * whole test, so an implementation that still answered the system would set the attribute and go on
 * painting the dark palette, and an attribute-only assertion would pass on it.
 */
test.describe('on a reader whose system asks for dark', () => {
  test.use({ colorScheme: 'dark' });

  test('choosing light repaints the page light', async ({ page }) => {
    await gotoSettled(page, '/');

    const systemsAnswer = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);

    await page.click(TOGGLE);
    expect(await storedTheme(page)).toBe('light');

    const chosen = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(chosen, 'the explicit choice did not override the operating system').not.toBe(systemsAnswer);

    // Named rather than merely "different": a page that had lost its tokens would also differ.
    await page.emulateMedia({ colorScheme: 'light' });
    const emulatedLight = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(chosen, 'the chosen light surface is not the light palette').toBe(emulatedLight);
  });
});

/**
 * The reason the click is not owned by a Blazor handler.
 *
 * Every route is prerendered, so the button is on screen and pressable long before the runtime
 * exists. A handler on the component would not answer until WebAssembly had downloaded and
 * re-rendered the header, and CounterPage.cs records what that looks like: a control that appears
 * to work and silently does not.
 *
 * The runtime is blocked outright rather than raced against. Clicking quickly and hoping the boot
 * has not finished would pass on a slow machine and stop testing anything on a fast one; refusing
 * _framework means the runtime CANNOT have started, so a click that works here worked without it.
 */
test('the control works before the runtime has started', async ({ page }) => {
  await page.route('**/_framework/**', (route) => route.abort());

  // Not gotoSettled: that waits for networkidle and for the fonts, and this page deliberately never
  // finishes loading. Nothing here measures text size, so the fonts do not matter.
  await page.goto('/', { waitUntil: 'domcontentloaded' });

  await expect(page.locator(TOGGLE)).toBeVisible();
  expect(
    await page.evaluate(() => 'Blazor' in window),
    'the runtime started anyway, so this test no longer proves what it claims',
  ).toBe(false);

  await page.click(TOGGLE);
  expect(await storedTheme(page), 'the prerendered control did not answer a click').toBe('light');
  expect(await visibleState(page)).toBe('Light');
});
