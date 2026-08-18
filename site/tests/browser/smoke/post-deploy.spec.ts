import {
  activeNavLinkCount,
  collectPageErrors,
  deployedRoutes,
  expect,
  installLayoutShiftCollector,
  readTwice,
  settled,
  test,
  totalLayoutShift,
} from './deployed';

const ROUTES = deployedRoutes();

// A guard on the derivation, not on the deployment, so it belongs here rather than as its own
// reported test case: it fails the whole run at collection time instead of leaving 0 route tests to
// be individually not run.
if (ROUTES.length === 0) {
  throw new Error('no route was derived from the content tree, so this suite would prove nothing');
}

for (const route of ROUTES) {
  test(`${route} hydrates into its own document without an error or a layout shift`, async ({ page }) => {
    const pageErrors = collectPageErrors(page);
    await installLayoutShiftCollector(page);

    const response = await settled(page, route);
    expect(response?.status(), `${route} did not load`).toBe(200);

    // The regression this guards against: a router that stops matching the route renders the
    // client's not-found content in place, which no check against the prerendered HTML alone can
    // see. This is the direct check -- the body itself did not fall back -- and `activeNavLinkCount`
    // below is the second, independent one: `SiteNav`/`DocsNav` derive the active link from
    // `NavigationManager` on their own, so a route whose active link still lit up correctly could
    // still have rendered the wrong body, and a route whose body rendered correctly could still have
    // lost its active link. Neither implies the other, which is why both are asserted rather than
    // either standing in for the pair.
    await expect(
      page.locator('h1'),
      `${route} rendered the not-found content in place of its document`,
    ).not.toHaveText('Page not found');

    expect.soft(
      await activeNavLinkCount(page),
      `${route} does not have exactly one active nav link after hydration`,
    ).toBe(1);

    expect.soft(pageErrors.errors(), `${route} logged a console or page error during load and hydration`).toEqual(
      [],
    );

    // A small tolerance rather than zero: the Layout Instability API is exact, but this deployment
    // is out of this suite's control in a way the local publish output is not, and a fraction of a
    // pixel of shift while a self-hosted face swaps in for the fallback stack is not the regression
    // this test exists to catch. 0.01 is well under WCAG's own "good" threshold of 0.1.
    expect.soft(
      await totalLayoutShift(page),
      `${route} shifted its layout when the client render replaced the prerendered markup`,
    ).toBeLessThan(0.01);
  });
}

test('/counter/ is interactive after hydration', async ({ page }) => {
  // The buttons are visible and clickable before the WebAssembly runtime starts and do nothing until
  // it does (CounterPage.cs), so a click that changes the count is a behavioural proof of hydration
  // rather than a proxy for it -- the same proof the manual baseline in this issue's history used.
  await settled(page, '/counter/');

  const count = page.locator('.demo-count');
  await expect(count, 'the counter did not render its readout').toHaveText('0');

  await page.getByRole('button', { name: 'Increment' }).click();

  await expect(count, 'a click on Increment did not change the rendered count').toHaveText('1');
});

test.describe('an unmapped path', () => {
  // There is no client-side router on this site to intercept a navigation: measured against this
  // deployment, clicking a link to another real, published route still produced a full page load
  // (the page's own JS state did not survive it). So "a client-side navigation to an unknown path"
  // and "a direct request for one" are the same code path here, and the two checks below both use
  // plain navigation rather than one of them simulating an in-app click.

  test('the /404 route itself renders inside the shell', async ({ page }) => {
    // Not a fallback: /404 is a real, prerendered document (`CopyPrerendered404` in #44 copies its
    // output to 404.html), reachable on its own. It answers 200, not 404.
    const pageErrors = collectPageErrors(page);
    const response = await settled(page, '/404');
    expect(response?.status()).toBe(200);

    await expect(page.locator('.site-nav')).toBeVisible();
    await expect(page.locator('h1')).toHaveText('Page not found');
    expect.soft(await activeNavLinkCount(page), 'no nav link may be active on /404').toBe(0);
    expect.soft(pageErrors.errors()).toEqual([]);
  });

  test('an unpublished path renders the same shell under a genuine 404 status', async ({ page }) => {
    // The failure mode this guards against is #44's: Cloudflare treating the deployment as a
    // single-page application and answering an unmapped path with 200 and the home page, which would
    // make every broken link on this site invisible to a crawler and to a reader alike.
    //
    // No collectPageErrors here, deliberately: the navigation's own top-level request is the 404
    // this test asserts, and Chromium logs that as a console error in its own right ("Failed to load
    // resource: the server responded with a status of 404"). That message is this test's premise,
    // not a defect, and filtering it out by pattern would be indistinguishable from filtering out a
    // real one that happened to share the words "404" and "resource".
    const response = await settled(page, '/this-route-does-not-exist-and-never-will-47/');
    expect(response?.status(), 'an unpublished path must answer 404, not fall back to the home page').toBe(404);

    await expect(page.locator('.site-nav')).toBeVisible();
    await expect(page.locator('h1')).toHaveText('Page not found');
  });
});

test.describe('status codes at the edge', () => {
  // The same four representative routes site.yml's sitemap step already names, so this reuses a
  // convention rather than inventing a second sample.
  const REPRESENTATIVE = ['/', '/counter/', '/docs/', '/docs/getting-started/'];

  for (const route of REPRESENTATIVE) {
    test(`${route} answers 200, read twice`, async ({ request }) => {
      const response = await readTwice(request, route, (r) => r.status() === 200);
      expect(response.status(), `${route} did not answer 200 on either read`).toBe(200);
    });
  }

  test('a bare path redirects to its trailing-slash form', async ({ request }) => {
    const bare = '/counter';
    const response = await readTwice(
      request,
      bare,
      (r) => r.status() >= 300 && r.status() < 400,
    );
    expect(response.status(), `${bare} did not redirect with a 3xx on either read`).toBeGreaterThanOrEqual(300);
    expect(response.status()).toBeLessThan(400);
    expect(response.headers()['location'], `${bare} redirected without pointing at its trailing-slash form`).toBe(
      `${bare}/`,
    );
  });
});
