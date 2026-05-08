---
name: seo-audit
description: Audit and improve SEO across a static site (Zola, Hugo, 11ty, Astro). Use when the user wants to improve search rankings, audit metadata, sweep meta descriptions, optimize Open Graph / Twitter cards, add JSON-LD / Schema.org markup, fix alt text, fix heading hierarchy, generate per-page SEO descriptions, or "do an SEO pass." Trigger on phrasings like "improve SEO", "audit SEO", "review meta descriptions", "SEO sweep", "make our pages rank better", "fix our metadata", "schema.org", "structured data", "JSON-LD", "rich snippets", "open graph", "twitter card", "search console". Codifies the difference between technical-baseline SEO (template work — tags, schema, canonical) and content-leverage SEO (descriptions, titles, internal links, cadence) so we don't burn time on diminishing returns.
---

# SEO Audit & Improvement

Most "SEO work" requests confuse two very different layers:

1. **Technical baseline.** Schema markup, meta tags, canonical, sitemap, robots, structured data. Necessary but not sufficient. Once it's in place, **more meta tags don't help.**
2. **Content leverage.** Per-page descriptions worth clicking on, keyword-front-loaded titles, internal linking, publication cadence, backlinks. This is where ranking actually moves.

Most sites under-invest in (1) (so the floor is low) and *over*-invest in fiddling with (1) once it's done (instead of moving to (2)). Don't fall into that trap. **Tier the work, do the technical pass once, then pivot to content.**

## When to use

- User asks to "improve SEO", "audit SEO", "do a meta sweep", "fix our metadata"
- User mentions "schema.org", "JSON-LD", "structured data", "rich snippets"
- User asks about Open Graph, Twitter cards, canonical URLs, search console
- A new content type is being added (news posts, hero pages, product pages) and the SEO meta needs to extend to it
- After deploy, when monitoring queries surface that descriptions/titles aren't clicking

## The diagnostic before improving

Don't reach for tags. Look at what's actually rendered.

```bash
# What does the live site emit per page archetype?
curl -sSL https://example.com/ \
  | grep -oE '<(meta|link|script)[^>]*(canonical|og:|twitter:|article:|application/ld)[^>]*>'

# How many JSON-LD blocks?
curl -sSL https://example.com/some-post/ | grep -c application/ld+json
```

Usually the first surprise: there's almost nothing there. Even "professional" WP sites with Yoast often fail to render schema on the static frontend (Yoast generates it, but Nuxt/Gatsby/etc. drops it). The bar to clear is **lower than expected**.

Then survey the existing field quality:

```bash
# Description lengths across content. Sweet spot is 140-160 chars.
for f in content/news/*.md; do
  desc=$(grep -E "^description = " "$f" | head -1 | sed 's/description = //; s/"//g')
  printf "%-40s | %3d | %s\n" "$(basename $f)" "${#desc}" "$(echo "$desc" | head -c 100)"
done
```

Look for:
- Descriptions <60 chars (too short — no information)
- Descriptions >180 chars (truncated in SERPs)
- Generic / flavor-text descriptions (e.g., in-character quotes that mean nothing in a search result)
- Missing descriptions (falling through to the site default)

## The work, in order of impact

### Tier 1 — technical baseline (do once)

Each item is a one-time template change. Once shipped, stop iterating.

**1. Block-based meta architecture.** Refactor `base.html` so every meta tag is a Tera/Jinja block with a config-level default. Per-page templates override only what they need. Avoids deeply-nested `{% if page %}{% set %}{% else %}` chains. Pattern from Pat's `~/code/omg-website/site/templates/base.html`:

```html
<link rel="canonical" href="{% block canonical %}{{ config.base_url | safe }}{% endblock canonical %}">
<meta property="og:type" content="{% block og_type %}website{% endblock og_type %}">
<meta property="og:title" content="{% block og_title %}{{ config.title }}{% endblock og_title %}">
```

Page templates override individual blocks: `news_post.html` says `{% block og_type %}article{% endblock %}` and that's it.

**`{% set %}` in Tera is scoped to its containing block.** Variables declared at the top of a child template *do not* survive into `{% block %}` overrides. Use `{% set_global %}` if you must cross that boundary, but the cleaner pattern is to inline the fallback chain inside each block — verbose but reliable.

**2. Canonical URL per page.** `<link rel="canonical">`. Use `page.permalink` (Zola idiom) rather than `current_url`. Cheap to add, prevents duplicate-content dilution.

**3. JSON-LD by page type.** Emit Schema.org markup as `<script type="application/ld+json">`:

| Where | Schema |
|---|---|
| `base.html` (sitewide) | `Organization` + `WebSite` |
| Home (game site) | `VideoGame` (name, genre, gamePlatform, publisher, trailer) |
| Home (studio site) | (skip — Organization covers it) |
| Blog/news post | `BlogPosting` (headline, datePublished, dateModified, author Person, publisher Organization, image) |
| Section/category page | `CollectionPage` (optional — limited rich-result value) |
| Nested pages (post, hero, legal) | `BreadcrumbList` |

Build the JSON-LD blocks inline in the page template's `extra_head` or `ld_extra` block. Use `json_encode()` on title/description fields to handle quotes safely:

```html
<script type="application/ld+json">
{
  "@context": "https://schema.org",
  "@type": "BlogPosting",
  "headline": {{ page.title | json_encode() | safe }},
  "datePublished": "{{ page.date }}",
  "publisher": { "@id": "{{ config.base_url | safe }}#organization" }
}
</script>
```

**Reference Pat's `~/code/omg-website/site/templates/post.html` for the BlogPosting recipe.** It's been validated against Google Rich Results Test.

**4. `article:` OG meta on posts.** `published_time`, `modified_time`, `author`, `section`. Slack/Discord/LinkedIn read these, JSON-LD alone isn't enough.

**5. OG / Twitter completeness.** `og:locale`, `og:image:alt`, `og:image:width` / `og:image:height`. Slack and LinkedIn won't render preview images without dimensions on some content sizes.

**6. `robots.txt`, `sitemap.xml`, atom feed link.** Most static generators emit these — verify they're served and reference each other.

### Tier 2 — content leverage (the real ranking work)

This is where SEO actually moves. Tier 1 sets the floor; Tier 2 raises the ceiling.

**7. Per-page meta descriptions.** Sweep every content file. Each description should be **140-160 characters**, **front-load the searchable term**, and read like ad copy. The description is what shows in the SERP under the title — it decides whether someone clicks. Generic site-wide fallbacks lose to deliberate per-page writing every time.

**Source-of-truth question.** If descriptions live in Markdown frontmatter and the content is regenerated from an upstream source (WP Extractor, Sanity sync, etc.), manual edits get overwritten. Solution: store SEO overrides in a separate file — for Zola, `config.toml` `[extra.seo]` keyed by slug works well:

```toml
[extra.seo.heroes]
reset = "Reset, The Bulwark — a Sci-Fi Tank hero in Spellcraft. Dictates battle by repositioning allies and enemies, then drops AoE damage on them."

[extra.seo.news]
"this-is-spellcraft" = "A free-to-play Real-Time Battler where you command a team of heroes and cast spells in real time to outplay your opponents."
```

Templates prefer the override, fall back to `page.description`, fall back to `config.description`. This survives `make extract` / sync runs and keeps SEO copy in one auditable place.

**Patterns for description writing:**
- Lead with the **subject** (hero name, post title) and **role** ("DPS hero", "Dev Blog post").
- Include the **product/site noun** (game name, studio name, category).
- Include the **genre / category** (Real-Time Battler, free-to-play, etc.).
- End with a hook — a verb phrase, a CTA, or a benefit.
- Avoid in-character flavor text. "Now you see me, now you're gone" is hilarious in context but useless in a SERP.

**8. Page titles front-loaded with intent keywords.** Compare:
- ❌ `"Reset — Spellcraft"`
- ✓ `"Reset, The Bulwark — Spellcraft"`
- ✓✓ `"Reset — Battle Droid Tank Hero | Spellcraft"`

Add the role / category to the title. Searchable terms should appear before the brand suffix. Don't go more than ~60 characters total or it truncates.

**9. Image alt text.** Decorative images (`alt=""`) are correct — accessibility win. Content images (hero portraits, news covers, product photos) need descriptive alt. Format: `{subject}, {role/category}` (e.g., `"Reset, The Bulwark hero in Spellcraft"`). Powers image search and accessibility simultaneously.

**10. Internal linking.** This is high-value and almost always neglected. Hero pages should link to related heroes (same role, same world). News posts should link to relevant hero pages. Build authority flow inside the site. **Often more impactful than any single schema addition.**

**11. Cadence.** Search engines reward sites that publish regularly. A site that posts once a month for two years outranks a site that publishes 50 posts in one month and goes silent. If launching, plan the first 3-6 months of posts.

### Tier 3 — off-template (the things that actually move rankings long-term)

Stop touching the templates. These are where the real work is.

**12. Search Console + Bing Webmaster Tools.** Submit `sitemap.xml`, verify ownership, monitor crawl errors. Search Console also shows **what queries people use to find you** — gold for Tier 2 description writing.

**13. Backlinks.** From press, partners, social. Single highest-impact off-page factor. Game databases (RAWG, IGDB) are easy wins — they backlink and create canonical entries. Wikipedia entries when notability allows.

**14. `sameAs` links in JSON-LD.** Connect your Organization / VideoGame / BlogPosting to other canonical sources (Steam page, Wikipedia, IGDB, social profiles). Helps search engines build the entity graph.

### Diminishing returns — only if specifically asked

- **FAQ schema** — only when there's a real FAQ section. Empty FAQ schema is a guideline violation.
- **Review schema** — only with real reviews.
- **Visible breadcrumbs UI** — accessibility nice-to-have; the BreadcrumbList JSON-LD already drives the SERP display.
- **`article:tag`** — minor; only worth it if the content is tag-heavy.
- **WebP / image optimization** — performance/CLS work. Real but small SEO win compared to the above.

## Anti-patterns

- **Adding more schema types when ranking is bad.** Schema is a *floor* signal — once present, more types don't help. The fix is content.
- **Yoast-style SEO HTML in CMS exports.** WordPress + Yoast generates rich `yoast_head` / `yoast_head_json` fields, but if your static frontend (Nuxt, Gatsby, etc.) doesn't render them, you're paying for Yoast and getting nothing on the public site. Either render them, or strip them from the cache and write your own (smaller surface, cleaner).
- **Per-CMS-export description regeneration overwriting hand-written SEO copy.** Solve once with an override store outside the regenerable content directory.
- **Inline ternary in Tera.** Tera doesn't support `(x if y else z)`. Use `{% if %}{% else %}{% endif %}` or filters.
- **Using `current_url` for canonical.** Works but `page.permalink` is the Zola idiom. Same for `og:url`.
- **Empty alt on every image.** Decorative images (icons, frames, accents) should be `alt=""`. Content images (hero portraits, product shots) should have descriptive alt. Mixing them up breaks both accessibility and image search.
- **Putting SEO descriptions in commit messages or PR descriptions.** Doesn't help search. Put them in the page.

## Verification

Lock the work in with end-to-end checks. The shape of the e2e check across page archetypes:

```python
seo_targets = [
    ("/", "website", ["Organization", "WebSite", "VideoGame"]),
    ("/news/", "website", ["Organization", "WebSite"]),
    ("/news/some-post/", "article", ["Organization", "WebSite", "BlogPosting", "BreadcrumbList"]),
]
for path, expected_og_type, expected_schemas in seo_targets:
    page.goto(f"{BASE}{path}")
    canonical = page.locator('link[rel="canonical"]').get_attribute("href") or ""
    assert canonical.endswith(path) or canonical.endswith(path.rstrip("/"))
    og_type = page.locator('meta[property="og:type"]').get_attribute("content")
    assert og_type == expected_og_type
    # Parse every JSON-LD block, flatten @graph, check expected @types
    found = []
    for s in page.locator('script[type="application/ld+json"]').all():
        data = json.loads(s.inner_text())
        if isinstance(data, dict) and "@graph" in data:
            found.extend(item.get("@type") for item in data["@graph"])
        elif isinstance(data, dict):
            found.append(data.get("@type"))
    for schema in expected_schemas:
        assert schema in found
```

Run the check across every page archetype. If the e2e is green, the technical baseline is in place — stop touching templates and pivot to content.

External validation tools to know:
- Google Rich Results Test (https://search.google.com/test/rich-results)
- Schema.org validator (https://validator.schema.org/)
- Facebook Sharing Debugger
- Twitter Card Validator
- LinkedIn Post Inspector

Run a sample page through Rich Results Test before declaring the JSON-LD pass complete.

## Working with this skill

Default workflow when invoked:

1. **Survey** — curl the live site (or local build) for each page archetype. Count canonical / og / twitter / JSON-LD tags. Identify what's missing.
2. **Audit current field quality** — description lengths, title formats, alt-text gaps. Use the bash one-liners above.
3. **Decide tier** — if technical baseline is missing, do Tier 1. If it's solid, pivot to Tier 2. Do not redo Tier 1 if it already works.
4. **Apply** — block-based template refactor for Tier 1 (one batch). Per-page description rewrites + title format + alt text for Tier 2 (one batch).
5. **Verify** — extend e2e to lock in the additions. Run Rich Results Test for at least one page from each archetype.
6. **Hand off the rest** — Search Console submission, backlinks, and content cadence are not template work. Flag them and stop.

Don't promise more than the floor + content sweep can deliver. Real ranking lift comes after deploy + months of monitoring. Set expectations accordingly.
