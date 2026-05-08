---
name: static-site-engineer
description: Front-end engineering on a static-site stack (Zola, Hugo, 11ty, Astro, Eleventy). Use when the user is working on the visual or structural surface of a static site — templates, SCSS/CSS, content authoring, small front-end JS, content schema, build/deploy of the front-end layer. Trigger on "Zola template", "Tera template", "Hugo template", "static site", "marketing site", "studio site", "landing page", "Markdown frontmatter", "TOML frontmatter", "blog template", "page template", "section template", "shortcode", "image processing", "SCSS pattern", "CSS pattern", "content authoring", "vanilla JS for the site", "build the site", "site is broken", "site doesn't render". Encodes the mental model that pays off across SSGs: read the source of truth first, reuse existing patterns over inventing new ones, verify visually not by diff, treat JS as a budget. Skip if the user is asking about a SPA framework (React/Vue/Svelte runtime), a CMS rendering layer (WordPress themes, Drupal), or build-side infrastructure (Cloudflare Pages, deploy pipelines, DNS).
---

# Static Site Engineer

Static-site front-end work — templates, CSS, content, small JS — looks deceptively simple. The traps are subtle: a template change that renders fine and breaks four pages you didn't think about; a CSS rule that wins a specificity battle nobody read; a frontmatter field whose type the generator infers wrong; a shortcode that loses its body's whitespace; an image that ships at 8MB because the engineer skipped the resize step. The patterns in this skill are the ones that make this kind of work durable.

## When to use

- Editing templates: `base.html`, page templates, partials, shortcodes (Tera/Liquid/Go templates/JSX/Astro components)
- Writing or refactoring SCSS / CSS
- Authoring content: Markdown with frontmatter, content directory structure, taxonomies
- Adding small front-end JS (carousels, modals, mobile menus, form handlers — not framework runtime)
- Fixing visual regressions
- Adding a new content type / page archetype
- Image / asset pipeline work (resize, optimize, lazy-load)
- Build configuration tweaks

## When NOT to use

- React/Vue/Svelte SPA work — different mental model (state, components, hydration)
- WordPress / Drupal theme development — CMS rendering layer is a separate beast
- Cloudflare/Vercel/Netlify deploy infrastructure, Workers, Pages Functions — that's an infra/deploy domain
- Backend/server logic — also separate

## The mental model

### 1. Read the source of truth before inventing

The deployed bundle is rendered output, not source. The minified hashes hide structure. Every time someone "matches a section" without reading the source, they reinvent something subtly different.

For greenfield work: read the design / Figma / PRD before writing a template.
For matching an existing site: find the source — the canonical Gatsby/Nuxt/React component, or the WP theme template, or the Figma frame. Read structure first, then layout.
For matching another page in the same site: find the existing page template, read its CSS classes, decide whether to reuse or extract.

When tempted to invent: stop, find the canonical, and *ask whether the existing pattern fits*.

### 2. Reuse over invent — 3 / 4 / 5

Three similar lines is fine. Three similar instances of a pattern is fine. **A fourth instance triggers extraction** into a shortcode, partial, or class. Don't preemptively factor — wait for the fourth instance to show you the right shape. Premature abstractions cost more than copies.

Concrete:
- Three news posts with cover-photo + title + intro: leave as inline template.
- Fourth post needs the same structure: factor into a shortcode or include.

CSS specifically: the project usually has an "Established patterns" reference (a section in the project CLAUDE.md, or a styleguide page). **Read it before adding a new class.** If the pattern exists, reuse the class. If it doesn't and you're inventing one, ask whether this is the third or fourth case.

### 3. Verify visually, not by reading the diff

Static-site front-end work is visual output. The diff doesn't tell you whether the page looks right. Build the site, view it in a browser, screenshot before/after if the change is non-trivial.

For methodical comparisons across many pages or breakpoints: capture screenshots of both the before and after via headless browser (Playwright) at desktop + mobile viewports, then diff visually. Don't try to assert pixel-perfect parity programmatically — describe structural / typographic / layout differences.

A typical screenshot loop:

```python
import asyncio
from playwright.async_api import async_playwright

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        for vp_name, vp in [("desktop", {"width": 1440, "height": 900}),
                            ("mobile",  {"width": 390,  "height": 844})]:
            ctx = await b.new_context(viewport=vp)
            page = await ctx.new_page()
            for path in PAGES:
                slug = path.strip("/") or "home"
                for source, base in [("local", "http://127.0.0.1:<port>"),
                                     ("canon", "<reference-url>")]:
                    await page.goto(f"{base}{path}", wait_until="domcontentloaded")
                    # Trigger lazy-loaded images by scrolling.
                    await page.evaluate("""async () => {
                      await new Promise((r) => {
                        let y = 0; const step = 400;
                        const iv = setInterval(() => {
                          window.scrollBy(0, step); y += step;
                          if (y >= document.body.scrollHeight) {
                            clearInterval(iv); r();
                          }
                        }, 80);
                      });
                    }""")
                    await page.evaluate("window.scrollTo(0, 0)")
                    await page.wait_for_timeout(300)
                    await page.screenshot(
                      path=f"/tmp/shots/{source}-{slug}-{vp_name}.png",
                      full_page=True)
            await ctx.close()
        await b.close()

asyncio.run(main())
```

Lazy-loaded images won't render in a full-page screenshot unless you scroll first. Always scroll before screenshotting.

### 4. JS is a budget, not a default

Static sites have a JS budget. The realistic interactive surface is small: mobile menu toggle, carousel/slider, modal trigger, newsletter form handler, scroll-triggered animations. **That's the whole budget.** Anything more should be questioned.

- Vanilla JS over a framework. No npm dep unless it's already there.
- CSS for transitions and snap-scroll over JS animation.
- `IntersectionObserver` for reveal-on-scroll instead of a scroll library.
- HTML attributes (`<details>`, `loading="lazy"`, `<dialog>`) over reimplementing in JS.

If asked to add a heavy interaction (drag-and-drop, complex form state, real-time anything), push back: is this a static-site feature, or is it actually a SPA feature that's leaked across the boundary?

### 5. Build before reporting done

The build catches:
- Template syntax errors (missing endblock, bad expression)
- Missing variables (frontmatter field referenced but not present on every page)
- Asset path errors (referenced image doesn't exist)
- Frontmatter parse errors (malformed TOML/YAML)

A green build is the **floor**, not the proof. After a green build, verify visually. After visual verification, run e2e if the project has them.

Common build commands across SSGs:
- Zola: `zola build`
- Hugo: `hugo`
- 11ty: `npx eleventy`
- Astro: `npm run build`
- Most projects wrap one of these in `make build` or `scripts/build`.

## Cross-SSG patterns

These come up often enough to be worth pre-loading.

### Frontmatter design

- Keep frontmatter shallow. Nested objects (`extra.author.first_name`) work but make queries awkward.
- Reserve top-level frontmatter for fields the engine cares about (`title`, `date`, `template`, `taxonomies`).
- Put everything else under `[extra]` (Zola), `params:` (Hugo), or equivalent. Don't pollute the top level.
- Date types matter: TOML dates without time become date objects in Tera; with time they're datetimes. Filter usage differs.
- Boolean defaults are tricky — explicit `draft = false` beats relying on absent → false in some engines.

### Section / template hierarchy

- A section is a directory with an index file (`_index.md` in Zola/Hugo). The section's template renders the listing; per-page templates render individual entries.
- Override hierarchy is type-specific in each engine. Zola: `templates/<section>/single.html` overrides `templates/page.html` for that section. Hugo: lookup order via theme + content type.
- When in doubt, dump `page` / `section` / `config` to see what's available. (`{{ page | json_encode | safe }}`).

### Shortcodes / partials

- Body shortcodes (`{% shortcode() %}body{% end %}`) are content-author-friendly. Use for repeated structural patterns (text blocks, image blocks, callouts, video embeds).
- Inline shortcodes (`{{ shortcode(args) }}`) for one-shot embeds (single image, video, button).
- Render the shortcode template to plain HTML — don't try to call other shortcodes from inside a shortcode unless the engine supports it explicitly.
- Whitespace in shortcode bodies matters. Some engines collapse it, some preserve it. Know which.

### Image processing

Most SSGs have built-in image processing: Zola's `resize_image()`, Hugo's `image.Resize`, Astro's `<Image>`. Use it.

- Source images go in a build-time directory (`assets/`, `_images/`). Avoid putting unprocessed full-res images in the public output.
- Output WebP when you can, with JPEG fallback for older browsers (rare these days).
- Always emit explicit `width` and `height` HTML attributes — prevents Cumulative Layout Shift, a Core Web Vitals metric.
- `loading="lazy"` for below-the-fold imagery.
- `alt=""` is *correct* for decorative images. Content images need descriptive alt. Don't blanket-set alt to "" or to the filename.

### CSS patterns to know

- **Edge-to-edge dark band.** Section drops max-width and gets horizontal padding; an `__inner` div holds max-width.
- **Bleeding callout.** Section uses outer container; callout column inside uses negative `margin-right` (or left) to extend past the inner container to the section's outer padding edge.
- **Alternating two-column rows.** `:nth-child(even)` to flip layout — not `--reverse` modifier classes. Reads the markup once, reverses every other row.
- **CSS scroll-snap carousel.** Pure CSS, no JS. `display: flex; overflow-x: auto; scroll-snap-type: x mandatory;` on the track; `scroll-snap-align: center` on each slide.
- **Decorative outline numerals / letters.** `-webkit-text-stroke: 1px <color>; -webkit-text-fill-color: transparent;`.
- **Avatar with monogram fallback.** `<img src="https://gravatar.com/avatar/<hash>?d=mp">` when email known; CSS-only `--monogram` div with initials when not.
- **Skip nav.** First focusable link in `<body>`, `position: absolute; left: -9999px;` until focused, then visible. Accessibility win, micro-SEO signal.

### Tera-specific gotchas

(For Zola sites and others using Tera-flavored templates.)

- `{% set %}` is **block-scoped**. Variables declared at the top of a child template don't survive into `{% block %}` overrides. Use `{% set_global %}` if you must cross that boundary, or inline the fallback chain inside each block.
- No inline ternaries. `{{ x if cond else y }}` doesn't exist in Tera. Use `{% if cond %}{{ x }}{% else %}{{ y }}{% endif %}` or filters.
- `default(value=...)` filter is your friend for missing-field fallbacks.
- `json_encode() | safe` is the right shape for emitting strings into JSON-LD or JS.
- `safe` filter on URLs is required when the URL contains `&` or other entity-encodable characters that would otherwise be escaped.

### Hugo-specific gotchas

- `index` vs `_index`: `index.md` is a regular page in a section; `_index.md` is a section's own page.
- `with` and `range` change the dot context. Use `$` to reach the global scope from inside.
- `safeHTML`, `safeURL`, `safeCSS` for trusted content. Default escaping is aggressive.
- `partialCached` for partials whose output is identical across pages. Big perf win on large sites.

### Build-time vs request-time

Static sites build once; output is static. This means:
- No request context (no IP, no cookies, no user state) at render time.
- All "dynamic" UI is JS in the browser, not server-rendered.
- A/B testing, personalization, geo-routing happen at the CDN edge, not in templates.

If a request is asked for "show different content based on logged-in state", the answer is JS-fetches-after-load (with a public API), or pre-render multiple variants and switch via JS. Don't try to bake auth-dependent state into the static output.

## Working with content

- Content is the source of truth for the page. Templates render content; they don't author it.
- Authors write Markdown with frontmatter. Make the frontmatter schema small and obvious — every field should have a clear purpose, and missing fields should fall back gracefully.
- Author-facing tools (Sveltia CMS, Decap CMS, Tina) save back to the content directory as Markdown. Design the frontmatter schema with the CMS UI in mind: structured fields > one big freeform "description".
- Per-content-type folders (`content/blog/`, `content/heroes/`, `content/products/`) are usually cleaner than flat content with a `type:` field.
- For blog/news posts, `content/blog/<slug>/index.md` (with images alongside) is the universal pattern. One folder per post. Cover image is `<slug>/cover.jpg`. Lets the CMS upload alongside without name collisions.

## Out of scope (hand off)

- Deploy infrastructure (Cloudflare Pages, Vercel, Netlify, Workers, edge functions, DNS, OAuth wiring) — that's a deploy/infra persona's domain.
- CMS auth flow (Sveltia, Decap, Tina with OAuth) — same.
- Core Web Vitals deep optimization (font loading strategies, edge caching, prerendering policy) — overlap with infra; consult before changing.
- Anything that requires a backend (databases, real-time data, server logic) — wrong stack; consider whether the page should still be static.

## Working with this skill

Default workflow when invoked:

1. **Identify the layer.** Is this a template change (Tera/Liquid), CSS, content, or JS? Don't mix layers in a single change unless the task genuinely requires it.
2. **Find the canonical / source-of-truth.** A live site to match? A Figma? An existing page that already does this? Read it before writing.
3. **Check for existing patterns.** Project CLAUDE.md often has a CSS patterns table. The fourth instance of a pattern is when you extract.
4. **Make the change.** Smallest possible — don't refactor adjacent code unless asked.
5. **Build.** Green build is the floor. Fix any template/parse errors before going further.
6. **Verify visually.** Browser, screenshot, viewports. Compare before/after for layout-affecting changes.
7. **Run e2e if the project has them.** Lock in any structural changes that an e2e check could catch (canonical, OG meta, content presence).
8. **Don't auto-commit.** Hand back to the user with a summary of what changed visually. Let them decide whether to commit.

## Anti-patterns

- **Inventing CSS without checking the existing patterns table.** Project styleguides exist; read them.
- **Reading the deployed minified bundle to figure out structure.** Find the source.
- **Adding npm deps for things CSS or vanilla JS handles.** Static sites have small JS budgets. Stay under it.
- **Reporting "looks fine" without opening a browser.** Templates lie. Diffs lie. Browsers don't.
- **Mixing template, content, and CSS changes in one commit.** Hard to bisect, hard to revert. Separate by layer.
- **Hardcoding strings the editor would want to change.** If a non-engineer would want to edit this string, it belongs in content or config, not the template.
- **Pixel-chasing without first asking whether the structural intent is met.** Spacing tweaks come after layout matches. Don't tune margins before the column layout is right.
- **`{% if %}{% endif %}` chains for what should be a `default(value=...)` filter or a config-table lookup.** Push complex logic out of templates into data.
