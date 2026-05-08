---
name: wp-migrate
description: Migrate a WordPress site (especially WP + ACF Pro, optionally with a Nuxt/Vue/React frontend) to a static site generator like Zola, Hugo, 11ty, or Astro. Use whenever the user mentions migrating, porting, converting, or re-platforming a WordPress site, or when they're wrestling with extracting content from a WP page-builder. Trigger on phrasings like "migrate WordPress to Zola", "convert this WP site to static", "the WP export is empty", "wordpress-export-to-markdown produced empty stubs", "ACF flexible content isn't in the XML", "the live site uses Nuxt over WordPress", "scrape the live site", "rebuild our marketing site", "switch off WordPress". Trigger even when the user doesn't say "migration" outright — if they have a WP XML export, a WP REST API endpoint they want to consume, or a Nuxt/Vue frontend reading from WordPress that they want to flatten, this skill applies. Encodes hard-won lessons about where WordPress content actually lives (REST API, not the XML, when ACF is in play), where the design CSS actually lives in Nuxt-fronted WP sites, what breaks invisibly when you skip the JS, and how to structure the work so iterations are cheap.
---

# WordPress → Static Migration

Migrating a WordPress site to a static generator looks straightforward — extract the content, rebuild the templates, ship — but the obvious sources are usually wrong, and the wrong assumptions cost the most when discovered late. This skill captures the patterns that work and the traps that don't, with specific attention to the ACF Pro + Nuxt/Vue stack that's common for modern WP marketing sites.

The single most important habit: **assume the obvious source is incomplete, then keep digging until you find the real one.** XML exports often don't have the visible content. CSS often isn't where the `<head>` says it is. Class names often don't carry their layout assumptions. JS often holds together more visual structure than it looks like. Default skepticism toward the first answer.

## When to use

This skill applies whenever the user wants to port a website from one rendering technology to another — typically away from a CMS or SPA toward a static generator. The flavors that come up most:

- WordPress (with or without ACF / Gutenberg / Beaver Builder / Elementor) → static
- Nuxt / Vue / React / Gatsby / Next.js → static HTML / Zola / Hugo / Astro
- Webflow / Squarespace / Wix → static
- Old hand-rolled PHP/Rails site → static

The diagnostic and architecture patterns below apply to all of these, with CMS-specific notes inline.

## The diagnostic before extraction

Run these checks **before** committing to any extraction approach. Each one rules out a wrong path that costs hours to discover after you've started.

### 1. Where does the visible content actually live?

The first question isn't "how do I extract" — it's "what produces the HTML the visitor sees." Three layers, only one of which you usually need.

- **Database / CMS storage** — what the editor writes into. WP wp_posts, ACF wp_postmeta, Webflow CMS items, Sanity, Contentful.
- **Build/render layer** — what turns storage into HTML. WP themes, Nuxt component templates, Webflow's renderer, GraphQL queries against a headless CMS.
- **Final HTML on the wire** — what the browser actually receives.

Start at the bottom (final HTML) by viewing source on the live site. Note what's static text vs. what's a JS-injected placeholder. If the visible body text appears in view-source, scraping the live site is viable. If it's empty divs that get hydrated, scraping won't work and you need to find the data source upstream.

### 2. Is there a CMS export / API / dump? Is it actually complete?

Don't trust an export until you've sampled it. For WordPress specifically:

- Run `wordpress-export-to-markdown` on the XML. If most posts come out as 6-line frontmatter stubs with empty bodies, the site uses ACF flexible-content or Gutenberg blocks the converter doesn't expand. **Don't push forward with this path.** ACF data lives in `wp_postmeta` as PHP-serialized strings that the XML carries but the converter doesn't unpack.
- Probe `https://[wp-host]/wp-json/wp/v2/types` (often public, often readable on staging without auth). If it returns `200 OK` with post types listed, the REST API is viable. Check whether ACF data is exposed by hitting `/wp-json/wp/v2/posts?per_page=1` and looking for an `acf` field on each post — if present, you have authoritative structured data.
- Check for multiple WP environments. Counts of posts/pages between dev/staging/production may differ wildly. The live frontend may be reading from a JSON cache fed by *one specific* environment. Confirm which env feeds the live site (often staging) before pulling content. Symptom of pulling from the wrong env: counts don't match what you see live.

For Nuxt/Vue/React/Gatsby:

- Look for a `__NUXT__` / `__INITIAL_STATE__` / similar JSON blob in view-source on the live site. That's the SSR data dump and often contains every piece of content the page renders. Parse it.
- Check whether there's a headless CMS upstream (Sanity, Contentful, Strapi). The frontend is usually a thin renderer over an API; pulling from that API is cleaner than scraping the rendered HTML.
- If neither of those — scraping rendered HTML with `wget` is the fallback, but be aware of the CSS-bundle problem (next section).

### 3. Where does the design system actually live?

This is the trap that costs the most time. The `<link rel="stylesheet">` tags in static HTML usually reference only the **critical bootstrap CSS** — typography, fonts, color tokens, top-level layout. The component-level layout rules (carousel, hero, modal, news cards, etc.) often live in **dynamically-imported CSS bundles** that aren't referenced in the static HTML at all.

Symptom: you scrape the HTML, link the CSS files referenced in `<head>`, build your own templates, and the page looks completely unstyled even though all your class names match the live site's.

Diagnosis for Nuxt/Vue:

```bash
# Pull the entry JS bundle
wget https://example.com/_nuxt/entry.<hash>.js

# Find every CSS file the JS references
grep -oE '[A-Za-z0-9_-]+\.[a-f0-9]+\.css' entry.<hash>.js | sort -u
```

You'll typically find 30-60+ CSS files. Pull all of them, concatenate into one stylesheet, and link from your base template:

```bash
{
  echo "/* Concatenated component CSS */"
  for f in static/_nuxt/*.css; do
    [ "$(basename "$f")" = "all.css" ] && continue
    echo "/* === $(basename "$f") === */"
    cat "$f"
    echo
  done
} > static/_nuxt/all.css
```

For React/Next.js, the equivalent is the `_app.js` bundle and `_buildManifest.js` listing route-specific JS chunks (CSS often inlined or alongside).

### 4. What JS state is the design relying on?

Some CSS rules are gated by JS-set classes. Without porting the JS, those gated elements stay invisible.

The most common pattern is reveal-on-scroll: `.fadeUp { opacity: 0 }` paired with `.onscreen .fadeUp { opacity: 1 }`. JS uses an IntersectionObserver to add `.onscreen` to sections as they enter the viewport. If you skip the JS, every element with `.fadeUp` (often including the navbar links and the hero copy) stays at zero opacity.

Always grep the borrowed CSS for these patterns before writing templates:

```bash
grep -oE '\.(onscreen|loaded|js-loaded|in-view|visible)[^{]+\{[^}]+\}' bundle.css | head
grep -oE '\.[a-zA-Z]+(__|--)[a-zA-Z-]+\s*\{[^}]*opacity:\s*0' bundle.css | head
```

Solution that works:

1. Add `<script>document.documentElement.classList.add('js')</script>` inline in `<head>` before the stylesheets — runs before first paint.
2. Add a no-JS fallback in your override CSS: `html:not(.js) .fadeUp, html:not(.js) .fadeIn { opacity: 1; transform: none; }` — accessibility for JS-disabled users.
3. Add a small IntersectionObserver in your site JS that adds `.onscreen` to `.animate` (or whatever the parent class is) sections as they enter viewport.
4. Add an inline script at end-of-`<body>` that synchronously reveals already-visible sections, so they don't flash invisible while the deferred observer JS loads.
5. For elements that should always be visible regardless of scroll (navbar, footer), hardcode `class="onscreen"` on them. The observer doesn't need to manage them.

### 5. What interactive UI depends on JS components you're not porting?

Modal video players, mobile hamburger menus, carousels, image lightboxes, accordion FAQs, newsletter forms, search bars — all common, all easy to drop without noticing because the static HTML doesn't show them as interactive. Symptom: a button on the live site that "does something" silently disappears in your port because you assumed it was decorative.

Walk through each page archetype on the live site **before** writing templates. List every clickable thing. For each, note whether it's:
- A simple link (port verbatim)
- A modal trigger (need to reimplement the modal)
- A form (need to wire a backend or accept a TODO)
- A widget that can be replaced with a CSS-only equivalent (carousel via scroll-snap, accordion via `<details>`)
- A widget that genuinely needs the original JS (rare; usually drop or rebuild)

Even broken-looking placeholders are better than missing UI. The button being there is more important than the modal working on day one.

## CMS-specific notes

### WordPress + ACF Pro

- The XML export's `<content:encoded>` field contains only Gutenberg block content. ACF flexible-content lives in `wp_postmeta` as PHP-serialized strings that the XML carries but `wordpress-export-to-markdown` doesn't unpack. If your sample export shows lots of empty post bodies, ACF is the reason.
- The REST API at `/wp-json/wp/v2/<type>` is the authoritative source. Custom post types may use a different `rest_base` than their slug — check `/wp-json/wp/v2/types/<slug>` for the `rest_base` field. (e.g., post type `hero` may be at `/wp-json/wp/v2/heroes`.)
- ACF flexible-content components come back as an array of objects with an `acf_fc_layout` discriminator. Each layout has its own field shape. Map each layout to an HTML render that matches the original CSS class structure (e.g., `text_block` → `<section class="textBlock"><div class="container--article"><div class="textBlock__inner">{rendered_html}</div></div></section>`).
- Image fields come back with full metadata including `url` (full size) and `sizes` (every WP-generated thumbnail). The internal URL host may rotate (CDN UUIDs, wpengine staging hosts) — normalize all of them to a single local prefix like `/_media/...` and download the canonical full-size variant.
- Yoast SEO data is at `yoast_head_json` on each post — useful for OG meta tags.

### Nuxt / Vue

- The entry JS bundle is the discovery point for the full CSS file list. The static HTML doesn't reference component CSS chunks.
- `_nuxt/` paths house all bundled assets — CSS, JS, fonts, images, decorative SVGs. Mirror the whole directory if storage isn't a concern.
- Component class names like `.heroHero__inner` or `.carouselStyle2__left` carry layout assumptions tied to the original Vue component DOM. **Borrow decorative classes (counters, frames, color tokens) freely; build layout classes yourself.** Don't try to wrap your scroll-snap carousel in `.carouselStyle2__inner` — that class enforces a 47%/53% column split designed for Swiper.js.
- **CSS bundle concat order matters more than CSS Cascade Layers.** When you concatenate Nuxt's per-component CSS files into one bundle, you have two collision problems: (1) several components define `header { ... }` for their own internal use and last-loaded wins, and (2) the Meyer-style reset in `entry.css` sets `margin: 0; padding: 0` on every block element including `section`/`header`, clobbering component layout rules if it loads after them. Cascade Layers seem like the answer but make this *worse* — unlayered author rules beat layered author rules in normal cascade, so putting global chrome in a layer drops it below the unlayered reset. Use plain ordered source-cascade instead:
  1. **Prelude** (load first): `entry.css` (reset + tokens), `plyr.css`, base utilities — anything that should be overridable.
  2. **Components**: every per-route bundle. Order within doesn't matter much; class specificity sorts most cases.
  3. **Postlude** (load last): `Header.css`, `Footer.css`, anything whose bare-element rules must win against component-internal rules.

  Critical: file-system glob ordering is alphabetical, which means lowercase `entry.css` loads AFTER capitalized `Header.css`. Build prelude/postlude lists explicitly; don't rely on `*.css` order.

### React / Next.js

- Static HTML is fairly content-rich (Next does SSR/SSG), so scraping is more viable than with pure SPA Vue.
- `_next/static/css/` houses the CSS bundles. Check `_buildManifest.js` for the per-route manifest.
- React component class names are often hashed (`.layout_module_hero__a3f`); CSS is hashed alongside, so borrowing classes works as long as you keep both together.

### Webflow / Squarespace / Wix

- Builders typically render the full visible HTML server-side, so scraping works for content.
- The CSS is usually one or two large stylesheets; download once, serve alongside.
- Forms and dynamic widgets often need rebuilding.

## The two-phase content storage pattern

What works best: **structured frontmatter for metadata + raw HTML for body**.

- Frontmatter holds the data the templates loop over: title, date, slug, cover image path, author, taxonomies, hero metadata (role, tagline, portrait URL), filter tags. This is what the *generator* needs to render index pages, sort, filter, generate feeds.
- Body holds pre-rendered HTML for flexible-content components, with the original class names preserved. This is what the *bundle CSS* needs to render the page correctly. The static generator passes raw HTML through markdown unchanged (Zola, Hugo, 11ty all do).

Why this beats either extreme:
- Pure-Markdown body loses the class structure the borrowed CSS expects. You'd have to rewrite every component layout in your template language.
- Pure-HTML page (no frontmatter) means you can't loop over posts in templates, can't generate index pages, can't sort by date.
- The hybrid lets you preserve the exact DOM the bundle expects while still treating each piece of content as a queryable record.

Example output file:

```markdown
+++
title = "This Is Spellcraft!"
date = 2023-03-10
slug = "this-is-spellcraft"
path = "news/this-is-spellcraft"
description = "Hello, gamers..."

[extra]
category = "Dev Blog"
author = "The Spellcraft Team"
read_time = 5
cover = "/_media/2023/03/22180408/tis-news.jpg"
+++

<section class="textBlock"><div class="container--article"><div class="textBlock__inner"><p>Hello, gamers!...</p></div></div></section>

<section class="blogImage animate blogImage--normal">...</section>

<section class="textBlock">...</section>
```

Templates wrap `{{ page.content | safe }}` (or equivalent) inside the outer wrapper the bundle expects (e.g., `<div class="componentWrapper sections">`), so the body's section classes resolve correctly.

## Architecture pattern that worked

```
project/
├── _migration/                  # source-of-truth fetched data (gitignored bin/obj/cache)
│   ├── api-cache/               # JSON pulled from REST API
│   │   ├── posts.json
│   │   ├── heroes.json
│   │   ├── pages.json
│   │   └── media-urls.txt       # download list, written by the extractor
│   ├── scraped/                 # HTML mirror of live site (fallback)
│   ├── Extractor/               # the tool that consumes api-cache and emits content/
│   ├── scrape.sh                # wget mirror, deterministic
│   └── css-bundle.sh            # concat _nuxt/*.css → all.css
├── content/                     # Zola/Hugo/11ty content
│   ├── _index.md                # home
│   ├── news/<slug>.md
│   ├── roster/<slug>.md
│   └── legal/<slug>.md
├── templates/                   # Tera/Go/Nunjucks templates
├── static/
│   ├── _media/                  # downloaded CMS images, paths preserved
│   ├── _nuxt/                   # downloaded CSS bundles, fonts, decorative SVGs
│   ├── overrides.css            # small file on top of the borrowed bundle
│   └── site.js                  # minimal interactivity (modal, observer, mobile nav)
├── config.toml
└── Makefile
```

The `_migration/` directory is the airlock: everything in there is fetched from upstream and is conceptually replaceable. Everything outside is committed code.

### Extractor tool

Build it in a real language with a CLI (C# / Go / Rust / Python — whatever the team uses). Two subcommands:

- `extractor fetch <cache-dir>` — hits the REST API, writes JSON files to cache. Idempotent.
- `extractor process <cache-dir> <content-dir>` — reads cache, writes Zola content files. Idempotent.

Splitting fetch from process means you can iterate on the extractor without re-hitting the API every time, and you can debug content issues without the network in the loop.

### Starter Makefile

A starter Makefile is in `assets/Makefile.template` — copy and adapt. It wires:

```
make fetch          → pulls API into _migration/api-cache/
make extract        → runs extractor against cache, writes content/
make mirror-media   → downloads referenced /_media/ images into static/_media/
make css-bundle     → concatenates _nuxt/*.css into static/_nuxt/all.css
make refresh        → fetch + extract + mirror-media
make build          → static generator build
make serve          → dev server
```

The point isn't the specific commands — it's that **every step is reproducible and bisectable.** When something breaks, you can re-run one phase to isolate the cause.

## What breaks invisibly

A short list of things that look fine in code review but break in the browser:

- **JS-gated reveal animations.** Sections stay at `opacity: 0` because the bundle expects an `.onscreen` class added by JS you didn't port. Always handle this; see diagnostic step 4.
- **Modal triggers.** The button is there, the modal markup isn't, clicking does nothing. Reimplement minimum viable modal even if just a YouTube embed.
- **Mobile nav.** Hamburger icon shows but doesn't toggle anything. ~10 lines of JS solves it.
- **Carousel/slider components.** Bundle CSS expects Swiper.js DOM (`.swiper-wrapper`, `.swiper-slide` with absolute positioning). Without Swiper's JS, all slides render stacked at position zero. Either rebuild as scroll-snap or include the original JS.
- **Newsletter form.** Posts to `index.html` or a stripped `action=""`. Wire to the real provider (Mailchimp/MailerLite/Klaviyo) or accept a TODO with a graceful "not connected yet" message.
- **Wishlist/CTA buttons with hover effects.** Often use sliding text-swap animations (`.textContainer` with two `.text` spans). The DOM is right but the bundle CSS positions the second span absolutely — make sure the parent has `position: relative`.
- **Trial/demo fonts.** Filenames containing `_Trial_` or `DEMO` are literally trial versions. Long-term production use requires either licenses or substitutes. Flag at the start; don't ship without resolving — many trial fonts are technically fine for development and commercial use without a license is a real legal risk.
- **Mobile responsive layouts.** Bundle CSS often hides desktop layouts under a media query and shows a parallel mobile layout. If you only port the desktop DOM, mobile users see nothing. Check for `@media (max-width: ...) { .desktopLayout { display: none } }` patterns.

## Final-mile concerns

Once the site builds and looks right, these are the things that block a real launch:

1. **Hosting & deploy target.** Cloudflare Pages, Netlify, Vercel, S3+CloudFront, or self-hosted. For a static Zola/Hugo site, Cloudflare Pages and Netlify both auto-build from a git push and are free/cheap. Pick before building, so DNS and CI can be set up in parallel.
2. **DNS cutover plan.** Where does the domain live now? When does it switch? Is there a rollback?
3. **Redirect map.** Old URL paths that no longer exist need 301s. Static hosts support `_redirects` (Netlify/Cloudflare) or a redirect file.
4. **Sitemap + robots.txt.** Most static generators produce these automatically; verify the output.
5. **Analytics.** GA4 / Plausible / Fathom snippet — add to base template once before launch, not after.
6. **Social meta / OG tags.** If the source CMS has Yoast or similar, the data is in the API response. Wire it to OG/Twitter card meta in your base template.
7. **Newsletter integration.** Get the actual provider details and API endpoints before launch — placeholder forms ship a broken first impression.
8. **Font licensing.** As above — resolve trial fonts before going public.
9. **Content edits during the migration window.** If editors are still updating WP, the migration content can drift. Either freeze content or run `make refresh` close to launch.

## SSIM is a guide, not a gate

If you're using SSIM (perceptual visual diff) to track convergence with the live
site, treat it as a *signal*, not a *target*. The metric will sometimes push you
toward replicating the live site's actual bugs:

- **Duplicated content.** Live's news list might render the featured post both in
  the "latest" hero card AND as the first card in the list below. SSIM will reward
  you for matching that. Don't — the duplicate is a UX bug, and a static port is a
  good chance to drop it.
- **Self-referential relationships.** A post's `related_news` array might list the
  post itself. Same logic — filter it.
- **Stale content.** The live site might show a hero or post that's been removed
  from staging. The right answer is what the staging API returns, not what live
  serves.

The diagnostic-and-fix discipline (find missing sections by comparing element
heights, fix wrong wrappers like `<h2>` vs bare `<p>`) is what *should* drive
SSIM up. When SSIM goes up because you copied a bug, that's a smell, not a win.

When SSIM dips after a fix that's editorially correct, accept the dip. A page
matching live's pixels at the cost of an obvious UX bug isn't actually closer to
done — it's worse.

## Anti-patterns to avoid

- **Trying to recreate JS-driven interactions in pure CSS when CSS-only doesn't fit.** Some carousels, modals, and rich widgets really do need JS. Don't burn a day on a CSS-only carousel that can't match the original feel; either bundle the original JS or rebuild simply.
- **Borrowing layout-driving classes from the source bundle.** Decorative classes (color tokens, typography utilities, accent borders) port well; layout classes (flex containers, grid wrappers, absolute-positioned slots) carry tight DOM assumptions and usually break.
- **Skipping the diagnostic and going straight to extraction.** Two hours of "where is the content actually" saves two days of "why is the content empty."
- **Assuming the WordPress XML is the source.** Confirm with a sample run before relying on it.
- **Pulling images one-time-only from the live site.** Use a deterministic mirror (a Make target with a URL list) so you can re-pull or add new ones idempotently.
- **Hand-editing content files instead of regenerating from the source.** Once `make extract` works, treat `content/` as derived. Edit the source (WP admin, headless CMS), re-extract.

## How to use this skill in practice

When the user opens a migration request:

1. Run the diagnostic checklist (above) to figure out where content and design actually live. Report findings before proposing an approach.
2. Confirm the target static generator (Zola, Hugo, 11ty, Astro). Ask if unclear.
3. Set up the directory structure (`_migration/`, `content/`, `templates/`, `static/`, `Makefile`).
4. Build the fetcher first — get the JSON cache (or HTML mirror) on disk.
5. Build the extractor next — translate cache to content files.
6. Build the templates — start with one archetype (e.g., a single news post), get it rendering with the borrowed CSS, then expand.
7. Address JS-gated invisibility before fighting individual pages — set up the IntersectionObserver and `.onscreen` plumbing once, save hours of "why doesn't this show up."
8. Walk through every page on the live site and enumerate interactive elements before declaring templates done.
9. Add the Makefile targets as you go so each phase is re-runnable independently.
10. Polish one page at a time, comparing against the live site. Use the diagnostic to keep finding what's missing.

This skill assumes the user has read access to the source site (live or admin). If they don't — first task is to get them to grant you what's available (admin login, application password, REST API URL, raw exports). Without source access, the migration is guessing.
