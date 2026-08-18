// A static file server over the `dotnet publish` output, started by playwright.config.ts.
//
// Written here rather than pulled from npm or shelled out to `python3 -m http.server` for two
// reasons. Node is already a hard requirement of this suite, so this adds no tool a contributor
// might not have, and none of the off-the-shelf servers reproduce the two Cloudflare Pages
// behaviours the published output is built around: a bare directory path redirects to its
// trailing-slash form, and an unknown path is answered with the top-level 404.html under a real
// 404 status rather than with the home page.
//
// This is emphatically NOT an emulation of Cloudflare's edge. `_headers` is ignored, so nothing
// here exercises the cache directives or the X-Robots-Tag rules, and no `_redirects` handling
// exists because the publish output must not contain that file at all (site.yml asserts its
// absence). Checks that depend on the edge belong in site/tests/browser/smoke, which runs against a
// deployment.
//
// Usage: node serve.mjs <wwwroot> <port>

import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import { extname, join, resolve, sep } from 'node:path';

const root = resolve(process.argv[2]);
const port = Number(process.argv[3]);

const CONTENT_TYPES = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.mjs', 'text/javascript; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.txt', 'text/plain; charset=utf-8'],
  ['.xml', 'application/xml; charset=utf-8'],
  ['.svg', 'image/svg+xml'],
  ['.png', 'image/png'],
  ['.ico', 'image/x-icon'],
  ['.woff2', 'font/woff2'],
  ['.wasm', 'application/wasm'],
]);

async function kindOf(path) {
  try {
    const info = await stat(path);
    return info.isDirectory() ? 'directory' : 'file';
  } catch {
    return 'missing';
  }
}

function send(res, status, path) {
  res.writeHead(status, {
    'content-type': CONTENT_TYPES.get(extname(path)) ?? 'application/octet-stream',
    'cache-control': 'no-store',
  });
  createReadStream(path).pipe(res);
}

const server = createServer(async (req, res) => {
  // The URL is only ever a path here; the query is discarded rather than parsed, because no route
  // in this site reads one.
  const requested = decodeURIComponent(new URL(req.url, `http://localhost:${port}`).pathname);

  // Containment check against the resolved root. `resolve` collapses "..", so a path that escapes
  // the publish output is rejected instead of read.
  const target = resolve(join(root, requested));
  if (target !== root && !target.startsWith(root + sep)) {
    res.writeHead(403).end();
    return;
  }

  const kind = await kindOf(target);

  if (kind === 'directory') {
    // Cloudflare Pages answers a bare directory path with a 308 to the trailing-slash form, and
    // the generated sitemap declares that form. Redirecting rather than serving the index directly
    // keeps relative URLs resolving from the same base they will on the deployment.
    if (!requested.endsWith('/')) {
      res.writeHead(308, { location: requested + '/' }).end();
      return;
    }
    const index = join(target, 'index.html');
    if ((await kindOf(index)) === 'file') {
      send(res, 200, index);
      return;
    }
  } else if (kind === 'file') {
    send(res, 200, target);
    return;
  }

  const notFound = join(root, '404.html');
  if ((await kindOf(notFound)) === 'file') {
    send(res, 404, notFound);
    return;
  }
  res.writeHead(404).end();
});

server.listen(port, () => {
  // Playwright's readiness probe watches for the port to answer, but this line is what makes a
  // failed start legible in a CI log (playwright.config.ts pipes this process's stdout through).
  console.log(`serving ${root} on http://localhost:${port}`);
});
