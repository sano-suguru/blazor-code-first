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
    'swept-characters',
    'boolean-attribute',
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


  // #150 swept every character class the HTML parsing specification singles out, to find whether any
  // besides the carriage return survives one path and not the other. The loop above already proves the
  // folded and unfolded spellings of the swept battery agree; this pins what that shared DOM contains,
  // so a defect that mangled both paths identically cannot pass by agreeing with itself.
  test('the swept character classes reach the DOM unchanged on both paths', async ({ page }) => {
    const folded = page.locator('#folded-swept-characters');

    await expect(folded.locator('span')).toHaveAttribute(
      'data-value',
      'a\u0001b\u0085c\uFEFFd\uFFFFe\tf  g',
    );

    const texts = await folded.locator('p').allTextContents();
    expect(texts).toEqual([
      'a\u0001b\u007Fc',
      'a\u0085b\u00A0c\u2028d\u2029e',
      'a\uFEFFb\uFFFDc',
      'a\uFFFEb\uFFFFc\uFDD0d\u{1FFFE}e',
      'a\tb\nc  d\u{1F600}e',
    ]);
  });

  // #158 admits one non-string constant into the fold, a bool, on the measurement that AddAttribute
  // renders true as name="" and omits the attribute for false. The loop above proves the two spellings
  // agree; this pins what they agree on, because the false half is invisible in the folded markup —
  // the fold writes nothing for it — and a fold that had dropped the true half as well would still
  // match an element path that had somehow done the same.
  test('a bool attribute is present with an empty value when true, and absent when false', async ({
    page,
  }) => {
    for (const container of ['#folded-boolean-attribute', '#unfolded-boolean-attribute']) {
      const probe = page.locator(container);

      await expect(probe.locator('input')).toHaveAttribute('disabled', '');
      await expect(probe.locator('input')).not.toHaveAttribute('hidden', /.*/);
      await expect(probe.locator('span')).toHaveAttribute('data-flag', '');
    }
  });

  // The remaining blocks each pin a *refusal*, so they do not go through the loop: the whole point is
  // that the static spelling does not fold, which makes a folded/unfolded comparison meaningless. Each
  // reads the two paths explicitly instead and measures what they actually do, which is what
  // CanRoundTrip's remarks cite. FoldParityTests pins the premise on the .NET side: that the static
  // container emits no markup frame and the Raw container emits exactly one.
  const readProbe = (page: import('@playwright/test').Page, id: string) =>
    page.evaluate((containerId) => {
      const container = document.getElementById(containerId)!;
      return {
        attribute: container.querySelector('span')!.getAttribute('data-value'),
        text: container.querySelector('p')!.textContent,
      };
    }, id);

  // A NUL diverges in two different shapes, which is what makes it worth measuring rather than
  // asserting a single "the parser replaces it" rule: it is dropped from text content and replaced with
  // U+FFFD in an attribute value. CanRoundTrip's remarks said U+FFFD for both, which was half wrong.
  test('the markup path drops a NUL from text and replaces it in an attribute value', async ({ page }) => {
    expect(await readProbe(page, 'markup-null')).toEqual({
      attribute: 'a\uFFFDb',
      text: 'ab',
    });
  });

  test('the element path keeps a NUL in both positions', async ({ page }) => {
    expect(await readProbe(page, 'element-null')).toEqual({
      attribute: 'a\u0000b',
      text: 'a\u0000b',
    });
  });

  test('a static NUL is refused by the fold, so it takes the element path', async ({ page }) => {
    expect(await readProbe(page, 'refused-null')).toEqual(await readProbe(page, 'element-null'));
  });

  // The lone surrogate is the one refusal with no divergence behind it. It never reaches the parser:
  // .NET's UTF-8 encoding of the render batch replaces it with U+FFFD, on both paths equally. These two
  // tests are what keep that stated reason honest — if a future runtime started delivering the
  // surrogate intact on one path only, they go red and the refusal acquires a real justification.
  test('a lone surrogate becomes U+FFFD before it reaches either path', async ({ page }) => {
    const markup = await readProbe(page, 'markup-lone-surrogate');
    const element = await readProbe(page, 'element-lone-surrogate');

    expect(markup).toEqual({ attribute: 'a\uFFFDb', text: 'a\uFFFDb' });
    expect(element).toEqual(markup);
  });

  test('a static lone surrogate is refused by the fold, so it takes the element path', async ({ page }) => {
    expect(await readProbe(page, 'refused-lone-surrogate')).toEqual(
      await readProbe(page, 'element-lone-surrogate'),
    );
  });

  // The leading byte order mark is #150's actual finding, and the only refusal here where the markup
  // path is the more faithful of the two. Nothing in HTML parsing is involved: the browser strips a BOM
  // in first position while decoding each frame string of the render batch. Folding is what moves the
  // character out of first position, which is why the fold has to refuse it.
  test('a leading BOM survives the markup path, where it is not the first character of the string', async ({
    page,
  }) => {
    expect(await readProbe(page, 'markup-leading-bom')).toEqual({
      attribute: '\uFEFFab',
      text: '\uFEFFab',
    });
  });

  test('a leading BOM is stripped on the element path, where the value is its own frame string', async ({
    page,
  }) => {
    expect(await readProbe(page, 'element-leading-bom')).toEqual({
      attribute: 'ab',
      text: 'ab',
    });
  });

  // The discriminating case. If the rule were "the element path drops a BOM", this markup string would
  // keep it; it does not, because the BOM is this string's first character. That is what makes the rule
  // positional, and what makes refusing a *leading* BOM the correct predicate rather than refusing the
  // character outright.
  test('the markup path also strips a BOM that starts its own frame string', async ({ page }) => {
    const text = await page.locator('#markup-leading-bom-at-string-start').textContent();
    expect(text).toBe('abone');
  });

  test('a static leading BOM is refused by the fold, so it takes the element path', async ({ page }) => {
    expect(await readProbe(page, 'refused-leading-bom')).toEqual(
      await readProbe(page, 'element-leading-bom'),
    );
  });

  // The carriage return is the refusal an author actually reaches: any verbatim string literal in a
  // file checked out with CRLF carries one. StaticMarkupSerializer.CanRoundTrip excludes a CR from the
  // fold because the HTML parser normalizes CRLF and a lone CR to LF during input-stream preprocessing,
  // before tokenization, so a folded CR would reach the DOM as LF where setAttribute and
  // createTextNode keep it. These three tests are the measurement behind that decision, taken in a real
  // browser rather than read off the spec: the first two show the two paths genuinely disagreeing, and
  // the third shows the refusal is what keeps the static spelling on the side that agrees.
  //
  // If a future browser stopped normalizing, the first test would go red — which is the signal that
  // CanRoundTrip could relax, not a defect. Read this block before changing that predicate.

  test('the markup path folds a carriage return away to a line feed', async ({ page }) => {
    // Html.Raw, so this is the same browser-side insertMarkup path a fold would have taken.
    expect(await readProbe(page, 'markup-carriage-return')).toEqual({
      attribute: 'a\nb',
      text: 'a\nb',
    });
  });

  test('the element path keeps a carriage return', async ({ page }) => {
    // setAttribute and createTextNode, which do no preprocessing.
    expect(await readProbe(page, 'element-carriage-return')).toEqual({
      attribute: 'a\rb',
      text: 'a\rb',
    });
  });

  test('a static carriage return is refused by the fold, so it takes the element path', async ({
    page,
  }) => {
    // Spelled entirely from constants and so foldable in every other respect. If CanRoundTrip ever
    // admitted a CR, this container would fold and match the markup result above instead.
    expect(await readProbe(page, 'refused-carriage-return')).toEqual(
      await readProbe(page, 'element-carriage-return'),
    );
  });
});
