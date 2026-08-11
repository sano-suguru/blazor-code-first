import { expect, gotoSettled, publishedRoutes, test } from './site-pages';
import type { Page } from '@playwright/test';

const ROUTES = publishedRoutes();

/**
 * Every text and background pair on the page, measured against WCAG AA.
 *
 * tokens.css writes every colour as OKLCH and app.css reads all of them through `var()`, so a
 * contrast regression here is one token edit away and reaches every page at once. Nothing else in
 * this repository can see it: the value in the stylesheet is not the value on screen once a
 * translucent layer or an inherited opacity is in the way.
 *
 * The walk composites deliberately rather than reading `background-color` off the element. Most
 * elements on this site have no background of their own, and several of the ones that do are
 * translucent, so the colour behind a run of text is the stack of its ancestors' backgrounds over
 * the page, not the nearest one that happens to be set.
 */
async function measureContrast(page: Page) {
  return page.evaluate(() => {
    type Rgb = { r: number; g: number; b: number };

    const WHITE: Rgb = { r: 255, g: 255, b: 255 };
    const BLACK: Rgb = { r: 0, g: 0, b: 0 };

    /**
     * Colour arithmetic happens on a canvas rather than by parsing the computed value.
     *
     * `getComputedStyle().color` is returned in the colour space it was authored in, and this site
     * authors every colour as OKLCH, so a computed value here reads `oklch(0.21 0.02 277)`. Pulling
     * three numbers out of that string and treating them as sRGB channels is not a rounding error,
     * it is a different colour: the first attempt at this check read a hue angle of 277 as 277 units
     * of blue and reported a ratio of 1.0 for every pair on the site. Painting the value instead
     * hands the conversion, the gamut clipping, and the alpha compositing to the same pipeline that
     * draws the page.
     */
    const canvas = document.createElement('canvas');
    canvas.width = 1;
    canvas.height = 1;
    const ctx = canvas.getContext('2d', { willReadFrequently: true })!;

    /** Paints `colour` at `opacity` over an opaque base and reads back what the screen would show. */
    const paint = (colour: string, base: Rgb, opacity = 1): Rgb => {
      ctx.globalAlpha = 1;
      ctx.fillStyle = `rgb(${base.r}, ${base.g}, ${base.b})`;
      ctx.fillRect(0, 0, 1, 1);
      ctx.globalAlpha = opacity;
      ctx.fillStyle = colour;
      ctx.fillRect(0, 0, 1, 1);
      ctx.globalAlpha = 1;
      const [r, g, b] = ctx.getImageData(0, 0, 1, 1).data;
      return { r, g, b };
    };

    /**
     * Recovers a colour's alpha by painting it over white and over black. The two results are the
     * same colour when it is opaque and 255 apart per channel when it is fully transparent, so the
     * gap is the transparency.
     */
    const alphaOf = (colour: string) => {
      const overWhite = paint(colour, WHITE);
      const overBlack = paint(colour, BLACK);
      return 1 - (overWhite.r - overBlack.r) / 255;
    };

    const luminance = ({ r, g, b }: Rgb) => {
      const channel = (raw: number) => {
        const v = raw / 255;
        return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
      };
      return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
    };

    const ratio = (one: Rgb, other: Rgb) => {
      const a = luminance(one);
      const b = luminance(other);
      return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
    };

    /**
     * The colour behind an element, or null when it cannot be computed from colours alone. A
     * background-image anywhere in the chain (a gradient counts) makes the answer position
     * dependent, and reporting a number for it would be reporting a guess.
     */
    const backdrop = (el: Element): Rgb | null => {
      const layers: string[] = [];
      for (let node: Element | null = el; node; node = node.parentElement) {
        const style = getComputedStyle(node);
        if (style.backgroundImage !== 'none') {
          return null;
        }
        const colour = style.backgroundColor;
        const alpha = alphaOf(colour);
        if (alpha > 0) {
          layers.push(colour);
        }
        if (alpha >= 1) {
          break;
        }
      }
      // The canvas under everything, painted outermost layer first. Anything still translucent at
      // the root composites onto it.
      let result = WHITE;
      for (let i = layers.length - 1; i >= 0; i--) {
        result = paint(layers[i], result);
      }
      return result;
    };

    /** Inherited opacity multiplies down the tree, so a faded ancestor fades this text too. */
    const effectiveOpacity = (el: Element) => {
      let opacity = 1;
      for (let node: Element | null = el; node; node = node.parentElement) {
        opacity *= parseFloat(getComputedStyle(node).opacity);
      }
      return opacity;
    };

    const failures: {
      selector: string;
      text: string;
      colour: string;
      background: string;
      ratio: number;
      required: number;
    }[] = [];
    const seen = new Set<string>();
    let measured = 0;

    for (const el of document.querySelectorAll('*')) {
      // Only elements that draw text themselves. A wrapper inherits its child's colour and would
      // otherwise be counted again with the same answer.
      const ownText = [...el.childNodes]
        .filter((n) => n.nodeType === Node.TEXT_NODE)
        .map((n) => n.textContent ?? '')
        .join('')
        .trim();
      if (!ownText) {
        continue;
      }

      const rect = el.getBoundingClientRect();
      if (rect.width === 0 || rect.height === 0) {
        continue;
      }
      // Off-canvas rather than merely below the fold: the skip link is parked outside the viewport
      // until it takes focus, and nothing reads it where it sits.
      if (rect.right <= 0 || rect.bottom <= 0 || rect.left >= document.documentElement.scrollWidth) {
        continue;
      }

      const style = getComputedStyle(el);
      if (style.visibility === 'hidden') {
        continue;
      }
      const opacity = effectiveOpacity(el);
      // Fully transparent text is not shown to anyone. The heading permalinks are the case that
      // matters: they are invisible under a fine pointer and opaque under a coarse one, which is
      // why this suite measures both.
      if (opacity < 0.05) {
        continue;
      }

      const background = backdrop(el);
      if (!background) {
        continue;
      }

      const key = `${style.color}|${opacity}|${background.r},${background.g},${background.b}|${style.fontSize}|${style.fontWeight}`;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      measured++;

      const foreground = paint(style.color, background, opacity);

      const size = parseFloat(style.fontSize);
      const weight = Number(style.fontWeight) || 400;
      // WCAG's large-text threshold: 18pt, or 14pt bold, in CSS pixels.
      const large = size >= 24 || (size >= 18.66 && weight >= 700);
      const required = large ? 3 : 4.5;

      const value = ratio(foreground, background);
      if (value < required) {
        failures.push({
          selector: `${el.tagName.toLowerCase()}${typeof el.className === 'string' && el.className ? `.${el.className.trim().split(/\s+/).join('.')}` : ''}`,
          text: ownText.slice(0, 40),
          colour: style.color,
          background: `rgb(${background.r}, ${background.g}, ${background.b})`,
          ratio: Math.round(value * 100) / 100,
          required,
        });
      }
    }

    return { measured, failures };
  });
}

/**
 * Both pointer types, because app.css branches on the pointer and the branch changes what is
 * visible. Under `(hover: none)` the heading permalinks become opaque, so the coarse pass is the
 * only one that measures them at all, and the `(pointer: coarse)` block resizes the hit targets
 * without touching their colours.
 */
const POINTERS = [
  { name: 'fine pointer', viewport: { width: 1280, height: 900 }, hasTouch: false },
  { name: 'coarse pointer', viewport: { width: 375, height: 900 }, hasTouch: true },
] as const;

for (const pointer of POINTERS) {
  test.describe(`under a ${pointer.name}`, () => {
    test.use({ viewport: pointer.viewport, hasTouch: pointer.hasTouch });

    for (const route of ROUTES) {
      test(`${route} meets WCAG AA contrast`, async ({ page }) => {
        await gotoSettled(page, route);

        const { measured, failures } = await measureContrast(page);

        expect(failures, 'a text and background pair falls below its WCAG AA threshold').toEqual([]);
        // A walk that stopped finding text would report zero failures and prove nothing. This is
        // the same defect class as a check that never runs (#163).
        expect(measured, 'no text was measured on this route, so the walk found nothing').toBeGreaterThan(3);
      });
    }
  });
}
