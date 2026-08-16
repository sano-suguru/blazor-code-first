import { expect, test } from '@playwright/test';

/**
 * The only check that exercises Blazor's DOM resynchronization, which is what
 * RenderViewEmitter's SetUpdatesAttributeName call turns on. When an event handler's setter normalizes
 * the value it receives, the DOM holds the text the user typed while the render tree holds the
 * normalized one. Ordinary diffing compares the new render tree against the previous render tree, sees
 * no change, and writes nothing — leaving the element displaying text the application already rejected.
 * SetUpdatesAttributeName names the attribute the browser changed, so the JS renderer sends the DOM's
 * own value back with the event (EventFieldInfo) and the diff compares against that instead.
 *
 * No .NET test layer can see this. bUnit's Input() writes the value that reaches the setter straight
 * into the AngleSharp DOM — its markup is a projection of the render tree, not a live DOM someone typed
 * into — so the divergence this repairs cannot be constructed there. That is measured, not assumed: an
 * earlier attempt to cover this from bUnit passed unchanged after the emission was replaced with a
 * no-op. Prerendering dispatches no events at all.
 *
 * `dotnet test` does not reach this suite — it needs a real browser, and BlazorCodeFirst.WebAppTests
 * asserts prerendered HTML through WebApplicationFactory. The `browser` job in
 * .github/workflows/ci.yml runs it on every pull request, which is what makes it a cover rather than a
 * check someone remembers (#163). As with fold-parity.spec.ts, the .NET side pins the premise and the
 * browser side measures the effect: BindResyncTests in BlazorCodeFirst.WebAppTests — which `dotnet test`
 * does run — asserts that TrimmingInputProbe really emits SetUpdatesAttributeName("value") on an
 * "oninput" binding, and HtmlBindGeneratorTests / RenderViewEmitterDecorationTests in Compiler.Tests pin
 * the emission itself. If those are red, a green run here means nothing.
 */

test.describe('a normalizing setter resynchronizes the DOM', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/bind-resync');

    // Type only once the circuit is live. The prerendered snapshot dispatches no events, so an
    // interaction against it would round-trip nothing and the resync could never run.
    await page.waitForSelector('#bind-resync-ready');
  });

  test('a value the setter normalizes away is written back over what the user typed', async ({
    page,
  }) => {
    const input = page.locator('#trimmed-input');

    // First keystroke, allowed to settle. This is what makes the second one measure resync: it leaves
    // the render tree holding "x", so the second round trip produces no change for ordinary diffing to
    // write. Starting from an empty field instead would pass with resync removed entirely, because ""
    // to "x" is a change the normal diff writes on its own.
    await input.fill('x');
    await expect(page.locator('#write-count')).toHaveText('1');
    await expect(page.locator('#field-value')).toHaveText('x');
    await expect(input).toHaveValue('x');

    // Second keystroke. The setter trims this to "x" — the value the render tree already holds — so no
    // edit is produced by comparing render trees, and the element would keep showing "  x  " forever.
    await input.fill('  x  ');
    await expect(page.locator('#write-count')).toHaveText('2');

    // The field never diverged; only the DOM did. Asserting this first means a failure below is
    // unambiguously about resynchronization and not about a round trip that did not happen.
    await expect(page.locator('#field-value')).toHaveText('x');

    // The measurement. This can only become "x" by Blazor writing it back, since the line above put
    // "  x  " there and the render tree's value did not change.
    await expect(input).toHaveValue('x');
  });
});

/**
 * The same measurement with an event modifier on the binding's own event. SetUpdatesAttributeName records
 * into the attribute frame immediately before it, so the modifier has to be emitted after the call and not
 * between it and the binding's event frame; emitted first, it would take the resynchronized name onto its
 * own frame and the repair below would vanish. The emission order is pinned on the .NET side by
 * HtmlDecorationGeneratorTests; this is where the order is shown to be the correct one (#370).
 */
test.describe('an event modifier on a binding does not cost it its resynchronization', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/bind-resync-modifier');
    await page.waitForSelector('#modified-binding-ready');
  });

  test('a value the setter normalizes away is still written back over what the user typed', async ({
    page,
  }) => {
    const input = page.locator('#modified-bound-input');

    await input.fill('x');
    await expect(page.locator('#modified-write-count')).toHaveText('1');
    await expect(page.locator('#modified-field-value')).toHaveText('x');
    await expect(input).toHaveValue('x');

    await input.fill('  x  ');
    await expect(page.locator('#modified-write-count')).toHaveText('2');
    await expect(page.locator('#modified-field-value')).toHaveText('x');
    await expect(input).toHaveValue('x');
  });
});

/**
 * The same measurement with a second binding on the same element. BCF3021 was shipped on the claim that
 * SetUpdatesAttributeName is stored per element, which would make this second binding overwrite the
 * first and delete the repair the first block measures. The storage is per frame (#162), so the repair
 * survives — and that is what this asserts.
 *
 * The second binding's own resynchronization is not measured here and cannot be: EventFieldInfo carries
 * only the element's own value or checked, so "data-committed" is never sent back. The emitter records
 * no resynchronized name for it, which BindResyncTests pins on the .NET side.
 */
test.describe('a second binding does not cost the first its resynchronization', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/bind-resync-two');
    await page.waitForSelector('#two-binding-ready');
  });

  test('a value the setter normalizes away is still written back over what the user typed', async ({
    page,
  }) => {
    const input = page.locator('#two-bound-input');

    await input.fill('x');
    await expect(page.locator('#two-write-count')).toHaveText('1');
    await expect(page.locator('#two-field-value')).toHaveText('x');
    await expect(input).toHaveValue('x');

    await input.fill('x ');
    await expect(page.locator('#two-write-count')).toHaveText('2');
    await expect(page.locator('#two-field-value')).toHaveText('x');
    await expect(input).toHaveValue('x');
  });
});
