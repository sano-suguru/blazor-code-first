import { expect, test } from '@playwright/test';

/**
 * The second door into Blazor's DOM resynchronization, opened by #307. Before it, no element-side
 * binding could reject its input: every value bound was a string, and every string parses. A numeric,
 * date or enum binding can now be handed something the framework's converter refuses, and what happens
 * then is documented by Microsoft rather than chosen here — "the unparsable value is automatically
 * reverted to its previous value when the bind event is triggered". This measures that this surface
 * reaches that behaviour, which its site documentation states.
 *
 * The mechanism is the one bind-resync.spec.ts already covers, but the scenario is not. There the
 * setter runs and rewrites the value, so the field moves and only the DOM diverges. Here the setter is
 * never called: the field does not move, the re-render produces exactly what the previous one did, and
 * the repair is the only thing that can put the element back. A test built on a normalizing setter
 * cannot construct that.
 *
 * No .NET layer sees this, for the reasons BindResyncView's remarks give at length: bUnit's markup is a
 * projection of the render tree rather than a live DOM, and prerendering dispatches no events.
 * BindResyncTests pins the .NET-side premises — that this probe registers "value" for resynchronization
 * and that its frame holds a formatted string — and the `browser` job in .github/workflows/ci.yml runs
 * this half on every pull request.
 */
test.describe('a value the converter rejects is reverted in the DOM', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/bind-reject');

    // Type only once the circuit is live. The prerendered snapshot dispatches no events, so nothing
    // would round-trip and the reversion could never run.
    await page.waitForSelector('#bind-reject-ready');
  });

  test('an unparsable keystroke puts the previous value back', async ({ page }) => {
    const input = page.locator('#rejecting-input');

    // A value that parses, first. This is the positive control: it proves the circuit round-trips at
    // all, so a failure below is about the reversion and not about an event that never arrived.
    await input.fill('41');
    await expect(page.locator('#reject-field-value')).toHaveText('41');
    await expect(input).toHaveValue('41');

    // Now one that does not. The setter is never called, so the field must not move.
    await input.fill('4x');
    await expect(page.locator('#reject-field-value')).toHaveText('41');

    // The measurement. Nothing in the render tree changed, so this can only become "41" by Blazor
    // writing it back over what was typed.
    await expect(input).toHaveValue('41');
  });

  /**
   * The consequence an author meets, and the reason the site documentation tells them to weigh
   * `onchange` against `oninput` for a numeric binding: with `oninput` the reversion runs on every
   * keystroke, so a decimal point never survives long enough to be followed by digits.
   */
  test('an int bound on oninput swallows a typed dot', async ({ page }) => {
    const input = page.locator('#rejecting-input');

    // Typed one character at a time, which is the whole point: fill() would deliver "4.5" as a single
    // input event and measure one rejection rather than the per-keystroke reversion an author meets.
    await input.pressSequentially('4.5');

    // "4" parses. "4." does not, and is reverted to "4" before the "5" arrives, so what the field ends
    // up holding is 45 — the digits, with the dot dropped between them.
    await expect(page.locator('#reject-field-value')).toHaveText('45');
    await expect(input).toHaveValue('45');
  });
});
