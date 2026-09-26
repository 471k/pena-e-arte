# Overnight Prompt — Search Visibility for tattooos.co (sitemap, noindex, canonicals, per-page metadata)

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt. It is self-contained: exact files, exact current code, exact
> target code, exact tests, exact docs to sync. Read the whole file before writing anything — later
> phases depend on decisions made in §2 and §3. **Mode: autonomous** for every code/config/test
> change in this file. **One hard stop:** every nginx change here ships UNTESTED by the current CI
> (see §1, "nginx is not exercised by CI") until Phase 7 lands its smoke test. Land Phase 7 first
> and make the nginx phases pass it. If you cannot run the frontend image's nginx (Docker daemon
> unavailable), stop after the non-nginx phases and report — do not merge nginx changes blind.

**Date logged:** 2026-09-26
**Requested by:** Phi
**Origin:** Engineering-consultation follow-up to the marketing site (PRs #188/#189/#190, live at
`https://tattooos.co`). Section 3 ("Search visibility") of the `TattooOS domain, website and search
to-do` Claude Docs page, steps 11–14 of its section 6 rollout checklist. The marketing pages exist
but search engines and link-preview bots still see one shared `<title>` for every route, a sitemap
URL that returns HTML, and no `noindex` on logged-in pages.

**Before starting:**
```bash
git status                                  # confirm clean; note the current branch
git checkout main && git pull
# Do NOT run `git add -A` for a checkpoint: it can sweep up untracked secrets/local files.
# Stage files by name only, throughout this whole task.
git checkout -b feat/search-visibility-2026-09-26
```
There is an untracked local file `docs/claude/TattooOS domain, website and search to-do.md` — it is a
copy of a Claude Docs page, not this repo's canonical copy. Leave it untracked; never `git add` it.

---

## 1. What's already there (verified 2026-09-26 against live source and production — do not re-derive)

**Raw HTML is one shared shell.** `frontend/index.html` ships one `<title>`, one description and
three static OG tags for every route; the SPA changes them only after JS runs. Its head today:

```html
<title>TattooOS — booking & studio management for tattoo shops</title>
<meta name="description" content="TattooOS — booking, deposits, consent forms, and client records for tattoo studios. Ditch the DMs and paper forms." />
<meta property="og:title" content="TattooOS" />
<meta property="og:description" content="TattooOS — booking, deposits, consent forms, and client records for tattoo studios. Ditch the DMs and paper forms." />
<meta property="og:image" content="/og-image.png" />
```
`og:image` is a **relative** URL (link previews need an absolute one) and `frontend/public/og-image.png`
**does not exist**. There is no `og:url`, `og:type`, `twitter:card` or canonical in the static head.
The title/description duplicate `frontend/src/shared/constants/legalEntity.ts` (`SITE_TAGLINE`,
`SITE_META_DESCRIPTION`) on purpose (a comment in `index.html` says why).

**`useDocumentMeta`** (`frontend/src/shared/utils/useDocumentMeta.ts`) sets `document.title` and injects
`og:title`, `og:type`, `og:url`, `twitter:*`, `description`, `og:image` and `<link rel="canonical">` on
mount, each tagged `data-doc-meta="1"`, and removes every `[data-doc-meta]` element before re-injecting
and on unmount. The static tags in `index.html` are NOT tagged, so today the head holds two `og:title`,
two `og:description` and two `og:image` tags on every page. `useStructuredData` (JSON-LD, tagged
`data-structured-data`) already exists and is used on the studio and artist pages.

**Canonicals are inconsistent.** `StudioPortfolioPage`, `ArtistPortfolioPage` and `DiscoverPage`
hard-code `https://tattooos.co/...` (six literals in `frontend/src/features/public/components/`).
`PublicContentLayout` (used by Home, Features, Pricing, the three use-case pages, FAQ, Contact and the
policy pages) builds the canonical from `window.location.origin`, so the same page names itself
canonical on **both** `app.tattooos.co` and `tattooos.co`.

**The sitemap exists but is unreachable and partly wrong.** `GET /sitemap.xml` is served by
`PublicEndpoints.cs` (`GetSitemap`, root-level, `AllowAnonymous`, `public-read` rate limit) from
`Pena_e_Arte.Application/Public/Queries/GetSitemapUrlsQuery.cs`. Verified live: **nginx has no location for
it**, so `https://app.tattooos.co/sitemap.xml` returns the SPA's `index.html` with `content-type:
text/html`. Defects in the generator itself:
- Studios are filtered on `IsActive` only, but the public studio page (`GetPublicStudioQuery` via
  `GetPublishedStudioBySlugAsync`) requires `IsActive && IsPublished`. An active-but-unpublished solo
  studio is listed in the sitemap and then renders nothing.
- Artists are filtered on the artist's own flags only; `GetPublicArtistQuery` also requires the artist's
  studio to be `IsActive`. A suspended studio's artists are listed and then 404.
- No marketing pages at all; no `Cache-Control`; slugs are interpolated into XML without escaping;
  `lastmod` is required by the record type.
- The base URL is the hard-coded constant `SiteBaseUrl = "https://tattooos.co"` in `PublicEndpoints.cs`.

**`frontend/public/robots.txt`** is `User-agent: *` / `Allow: /` / `Sitemap: https://tattooos.co/sitemap.xml`.

**nginx (`frontend/nginx.conf.template`).** One `server` block, `server_name _`, `listen 8080`. Every
location repeats `add_header X-Robots-Tag "${IS_STAGING}" always;` (empty on production ⇒ nginx omits the
header; the literal noindex value on staging). `location /` is the SPA fallback
(`try_files $uri $uri/ /index.html`). There is no `map`, no `sitemap.xml` location, and no per-route
handling. The runtime is `nginxinc/nginx-unprivileged:1.27-alpine`; the template is rendered by the
image's `envsubst` step, which only substitutes variables defined in the container environment, so
nginx's own `$uri`, `$host`, `$http_user_agent` etc. are left alone.

**nginx is not exercised by CI.** `frontend/playwright.config.ts` runs e2e against `pnpm dev` (Vite,
`http://localhost:5173`), never nginx. The `docker-build` job in `.github/workflows/ci.yml` only builds
the frontend image (`push: false`, no `load`, no run). There is no local `nginx`, and Docker Desktop has
been unreliable on this machine. Consequence: a broken `nginx.conf.template` would first be noticed in
the production deploy. **Phase 7 closes this gap and must land with (or before) any nginx change.**

**Routes and what is private.** Authenticated routes are the relative children of the app root in
`frontend/src/app/router.tsx`: `account appointments artists billing book booth-rent campaigns clients
conduct-reports consent dashboard deposit-rules designs earnings feedback forms gift-cards help-insights
intake intake-form-builder me messages my-studios new notifications packages pay payments plans platform
promo-codes referrals reports schedule services studios subscribe subscriptions traffic waitlist`
(plus dynamic `:id`-style children). Top-level public/utility routes: `/ /features /pricing /use/booking
/use/deposits /use/consent-forms /faq /contact /privacy /terms /refund-policy /discover /map /s/:slug
/s/:slug/gift-cards/buy /artist/:slug /share/:token /embed/:studioSlug /login /register /client-register
/forgot-password /reset-password /verify-email /confirm-change-email /unsubscribe`. Recompute this list
from `router.tsx` at the start of Phase 2 — it is the input to the noindex map and its guard test.

**Owner-facing public links.** `QrCodeSection.tsx` and the API's `GetStudioQrCodeQuery` already use the
apex (`https://tattooos.co/s/{slug}`). `ArtistDetailPage.tsx:640` and `ArtistSocialTab.tsx:75` build the
artist's public link from `import.meta.env.VITE_PUBLIC_URL ?? window.location.origin`, which is
`https://app.tattooos.co` in production. `EmbedCodeCard.tsx`/`EmbedPage.tsx` also use `VITE_PUBLIC_URL`
(the embed widget is served from the app host on purpose — leave it).

**Toolchain facts.** CI and the frontend Dockerfile use Node 24; `tsconfig` has `erasableSyntaxOnly:
true`, so Node 24's built-in TypeScript type stripping can run erasable-syntax `.ts` files directly with
no new dependency. `@playwright/test` is already a devDependency. The service worker (`public/sw.js`)
is network-first for navigations, so per-route HTML files are picked up on deploy.

---

## 2. Decisions already made — implement as specified, do not re-litigate

1. **No SSR and no third-party prerender service.** Metadata for the *static* routes is produced at build
   time by a small Node script; the *dynamic* studio/artist pages get a crawler-facing HTML shell from
   the API (Phase 5, separate PR). The React app itself stays a client-rendered SPA.
2. **The apex `https://tattooos.co` is the one canonical host** for every public page. The pages are
   served on both hostnames; canonical links resolve the duplication. `app.tattooos.co` stays the
   application host.
3. **Do not `Disallow` private app routes in `robots.txt`.** A disallowed URL is never fetched, so its
   `noindex` header would never be seen. Private routes are handled with an `X-Robots-Tag: noindex,
   nofollow` response header from nginx. `robots.txt` disallows only `/api/`, `/hubs/`, `/hangfire`.
4. **One `SITE_URL` constant on the frontend** replaces every hard-coded `https://tattooos.co` literal.
5. **The og-image is a generated placeholder brand asset** (script + committed PNG), clearly marked as
   replaceable by marketing. Do not invent artwork beyond the wordmark and tagline.
6. **Page titles and descriptions live in one manifest** (`siteRoutes.ts`) that both the pages and the
   build script read, so raw HTML and client-side metadata cannot drift apart.
7. **No new npm/NuGet package.** If you reach for one, stop and flag it.

## 3. Decisions for Phi to confirm before this runs (defaults apply if unanswered)

- **D1 — Phase 5 (crawler HTML shell for `/s/:slug` and `/artist/:slug`).** Default: build it, as a
  **separate second PR**, after Phases 1–4, 6 and 7 are merged and verified. It touches nginx routing for
  the two most important public page types and is the riskiest piece. Answer "skip Phase 5" and it is
  deferred to its own later prompt.
- **D2 — Owner-facing public links use the apex.** Default: yes (`ArtistDetailPage`, `ArtistSocialTab`
  switch from `VITE_PUBLIC_URL` to `SITE_URL`), so the link owners share matches the canonical and the
  sitemap. The embed snippet stays on `VITE_PUBLIC_URL`.
- **D3 — 301 the marketing paths from the app host to the apex.** Default: **no**. Canonical links are
  sufficient and a redirect at the app host adds routing risk. Revisit if Search Console shows the app
  host outranking the apex.

---

## 4. Scope boundary — do not touch

- `PublicPageHeader.tsx` (the mobile wrap is a known, separate item), `SiteFooter.tsx`, the marketing
  pages' visible copy, and `planHighlights.ts` (copy is waiting on the marketing project).
- `k8s/**` and `.github/workflows/cd.yml`. The only workflow change allowed is the smoke test step in
  `ci.yml` (Phase 7).
- `frontend/public/sw.js`, the embed widget's URL (`VITE_PUBLIC_URL` usage in `EmbedCodeCard.tsx` /
  `EmbedPage.tsx`), and any authenticated page.
- Google Search Console, Google Analytics, Meta/TikTok/X/LinkedIn verification, and footer social links:
  manual/third-party steps (to-do steps 15–19). Do not attempt them.
- Do not add structured data (JSON-LD) to marketing pages in this pass; that is a follow-up.

## 5. Constraints (restated, apply throughout)

- TypeScript strict, no `any`, explicit types; C# with explicit types (no `var` for non-obvious types);
  no `console.log` / `Console.WriteLine` in production paths; structured Serilog only.
- Never log PII. Nothing here touches personal data except Phase 5, which renders studio/artist public
  profile fields already exposed by the public API — and must **HTML-encode every dynamic value**.
- Every new `AllowAnonymous` endpoint needs a row in `docs/claude/architecture.md`'s "AllowAnonymous
  Exceptions" table in the same change (rule 2). Phase 5 adds two; Phase 1 adds none.
- Every code change ships with tests in the same PR (§10). Stage files by name; never `git add -A`.
- Help sync (rule 7) is part of done (§11). Industry-benchmark note (rule 6) is part of done (§12).
- `dotnet format --verify-no-changes` is enforced by CI and the repo uses CRLF: after writing new `.cs`
  files run `dotnet format whitespace "Pena e Arte.slnx" --no-restore --include <files>` and re-verify.

---

## 6. Phase 1 — Sitemap: fix the generator and make it reachable

### 6.1 — Backend: correct URLs, add marketing pages, escape, cache

**File:** `Pena_e_Arte.Application/Public/Queries/GetSitemapUrlsQuery.cs`

Change `SitemapUrlEntry` so `LastModified` is nullable (static pages have no meaningful date), add the
marketing path list, and fix both filters:

```csharp
public record SitemapUrlEntry(string Path, DateTime? LastModified);

public static class SitemapPaths
{
    // Public, indexable marketing surfaces. Policy pages stay indexable but are deliberately not
    // listed. Keep in step with frontend/src/shared/seo/siteRoutes.ts (a frontend test asserts it).
    public static readonly IReadOnlyList<string> Marketing =
    [
        "/", "/features", "/pricing", "/use/booking", "/use/deposits",
        "/use/consent-forms", "/faq", "/discover", "/contact",
    ];
}
```

In the handler: marketing entries first (`LastModified = null`); studios filtered on
`s.IsActive && s.IsPublished` (mirrors `GetPublishedStudioBySlugAsync`); artists keep
`a.DeletedAt == null && a.IsActive && a.Slug != null` **and** require an active studio:

```csharp
.Where(a => a.DeletedAt == null && a.IsActive && a.Slug != null
    && db.Studios.IgnoreQueryFilters().Any(s => s.Id == a.StudioId && s.IsActive))
```

(Artists deliberately do NOT require `IsPublished` — see architecture.md, "IsActive vs IsPublished".)
Update the existing "Approved:" comment and architecture.md `IgnoreQueryFilters()` row #40 to say the
handler also joins the studio's active/published state. No new row is needed: same handler, same purpose.

**Extract the XML into a pure, testable type** — `Pena_e_Arte.Application/Public/SitemapXmlWriter.cs`:

```csharp
public static class SitemapXmlWriter
{
    public static string Build(IEnumerable<SitemapUrlEntry> urls, string siteBaseUrl) { /* ... */ }
}
```
It emits the same `<urlset>` as today, XML-escapes each `<loc>` (`System.Security.SecurityElement.Escape`),
and writes `<lastmod>` only when `LastModified` is not null. Use `StringBuilder sb = new();` (explicit type).

**File:** `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` — `GetSitemap` takes `HttpContext http`, calls
`SitemapXmlWriter.Build(urls, SiteBaseUrl)`, sets `http.Response.Headers.CacheControl = "public,
max-age=3600";` and still returns `Results.Text(xml, "application/xml")`. Keep the route, `AllowAnonymous`
and `public-read` rate limit unchanged.

### 6.2 — nginx: proxy `/sitemap.xml` to the API

**File:** `frontend/nginx.conf.template` — add, next to the other proxy locations (an exact-match
location outranks `location /`):

```nginx
    # Sitemap is generated by the API (GetSitemapUrlsQuery) — proxy it instead of letting the SPA
    # fallback below answer with index.html (text/html), which is what happened before 2026-09-26.
    location = /sitemap.xml {
        proxy_pass http://${BACKEND_HOST}:${BACKEND_PORT};
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        add_header X-Robots-Tag "${IS_STAGING}" always;
    }
```
`proxy_pass` with no URI part forwards the original `/sitemap.xml`, which is exactly where the API serves
it. The proxy headers match the `/api/` block, so the forwarded-headers chain (`ForwardLimit: 3`) is
unchanged.

---

## 7. Phase 2 — `robots.txt` and `noindex` for private routes

### 7.1 — `frontend/public/robots.txt`

```
User-agent: *
Allow: /
Disallow: /api/
Disallow: /hubs/
Disallow: /hangfire

Sitemap: https://tattooos.co/sitemap.xml
```
(Decision §2.3: private SPA routes are NOT disallowed here.)

### 7.2 — nginx: `X-Robots-Tag: noindex, nofollow` for private routes

**File:** `frontend/nginx.conf.template`. Add a `map` **above** the `server {` block (the rendered file is
included at `http` level, where `map` is legal):

```nginx
# Private/utility SPA routes are noindex. Kept in step with frontend/src/app/router.tsx — a vitest
# guard (seoRouteCoverage.test.ts) fails if a router path is in neither this map nor the public list.
map $uri $private_robots {
    default "";
    ~^/(account|appointments|artists|billing|book|booth-rent|campaigns|clients|conduct-reports|consent|dashboard|deposit-rules|designs|earnings|feedback|forms|gift-cards|help-insights|intake|intake-form-builder|me|messages|my-studios|new|notifications|packages|pay|payments|plans|platform|promo-codes|referrals|reports|schedule|services|studios|subscribe|subscriptions|traffic|waitlist)(/|$) "noindex, nofollow";
    ~^/(login|register|client-register|forgot-password|reset-password|verify-email|confirm-change-email|change-email|change-password|unsubscribe|share|embed|hangfire)(/|$) "noindex, nofollow";
}
```
Then, in **`location /` only** (the SPA fallback that serves every private route), replace the existing
header line with the combined value so staging's blanket noindex and the per-route noindex both apply:

```nginx
        add_header X-Robots-Tag "${IS_STAGING}$private_robots" always;
```
Verify while implementing: (a) `${IS_STAGING}` is substituted by envsubst but `$private_robots` is left for
nginx; (b) an empty concatenation makes nginx omit the header (as it does today for `""`); (c) a leading
segment must match exactly (`/s/...` and `/discover` are not caught by the private regexes; note that
`/gift-cards` is private only as a top-level path — `/s/{slug}/gift-cards/buy` is public and unaffected).

---

## 8. Phase 3 — One `SITE_URL`, correct canonicals, absolute og-image

1. **`frontend/src/shared/constants/legalEntity.ts`** — add `export const SITE_URL = "https://tattooos.co";`
   next to `SITE_TAGLINE`.
2. Replace every hard-coded apex literal with `SITE_URL`: `StudioPortfolioPage.tsx` (lines ~51, ~59),
   `ArtistPortfolioPage.tsx` (~52, ~61), `DiscoverPage.tsx` (~156), `QrCodeSection.tsx` (~52). Re-grep for
   `https://tattooos.co` in `frontend/src` at the end; the only remaining literals may be tests and the
   manifest/constants files.
3. **`PublicContentLayout.tsx`** — canonical becomes `` `${SITE_URL}${canonicalPath}` `` instead of
   `window.location.origin`. This is the fix for the duplicate-canonical problem on marketing and policy
   pages. Update any existing test that asserted the origin-based canonical.
4. **og-image.** Add `frontend/scripts/generate-og-image.mjs` that renders a fixed 1200×630 HTML template
   (dark background `#0b0b0f`, the wordmark from `frontend/public/favicon.svg`, `SITE_TAGLINE` in the
   app font stack) with the existing `@playwright/test` Chromium and writes `frontend/public/og-image.png`.
   Commit the generated PNG (target < 100 KB). It is a placeholder — say so in a header comment and in the
   PR. It is run manually, not on every build.
5. **`frontend/index.html`** — `og:image` becomes the absolute `https://tattooos.co/og-image.png`.

---

## 9. Phase 4 — Per-route metadata in the raw HTML (static routes)

The routes whose metadata is static — `/features /pricing /use/booking /use/deposits /use/consent-forms
/faq /discover /contact /privacy /terms /refund-policy` — get their own `<title>`, description, canonical
and OG/Twitter tags in the HTML served for that URL. `/` keeps the defaults in `index.html`.

### 9.1 — The manifest (single source of truth)

**File:** `frontend/src/shared/seo/siteRoutes.ts` (new; dependency-free, erasable syntax only, no `@/`
imports so Node can run it directly):

```ts
export const SITE_URL = "https://tattooos.co";

export interface RouteMeta {
  path: string;          // "/pricing"
  title: string;
  description: string;
}

export const STATIC_ROUTES: ReadonlyArray<RouteMeta> = [
  { path: "/features", title: "Features — TattooOS", description: "..." },
  // ... one entry per static route above
];
```
Move each page's current `title`/`description` strings here **verbatim** (the marketing pages' strings
are in their components; `Contact`/`Privacy`/`Terms`/`Refund`/`Discover` have their own). Each page then
reads its entry (`ROUTE_META["/pricing"]`, a `Record` built from the array) instead of an inline string.
`SITE_URL` here and in `legalEntity.ts` must be equal — a unit test asserts it (or have `legalEntity.ts`
re-export it if `tsconfig` allows `.ts`-extension imports; pick whichever type-checks).

### 9.2 — Pure renderer

**File:** `frontend/src/shared/seo/renderRouteHtml.ts` (new, dependency-free):
`renderRouteHtml(template: string, route: RouteMeta): string`. It replaces the region between the markers
below with the route's head (title, description, canonical, `og:type`, `og:url`, `og:title`,
`og:description`, absolute `og:image`, `twitter:card`, `twitter:title`, `twitter:description`,
`twitter:image`), HTML-attribute-escaping every value, and **throws** if either marker is missing.

### 9.3 — `index.html`: mark the region and stop the duplicates

Wrap the current title/description/OG tags in markers and add the missing tags. **Every `<meta>`/`<link>`
in the region carries `data-doc-meta="1"`** so `useDocumentMeta`'s cleanup replaces them instead of
duplicating them (this also removes today's double `og:title`):

```html
<!-- seo:head:start -->
<title>TattooOS — booking & studio management for tattoo shops</title>
<meta data-doc-meta="1" name="description" content="TattooOS — booking, deposits, consent forms, and client records for tattoo studios. Ditch the DMs and paper forms." />
<link data-doc-meta="1" rel="canonical" href="https://tattooos.co/" />
<meta data-doc-meta="1" property="og:type" content="website" />
<meta data-doc-meta="1" property="og:url" content="https://tattooos.co/" />
<meta data-doc-meta="1" property="og:title" content="TattooOS — booking & studio management for tattoo shops" />
<meta data-doc-meta="1" property="og:description" content="TattooOS — booking, deposits, consent forms, and client records for tattoo studios. Ditch the DMs and paper forms." />
<meta data-doc-meta="1" property="og:image" content="https://tattooos.co/og-image.png" />
<meta data-doc-meta="1" name="twitter:card" content="summary_large_image" />
<meta data-doc-meta="1" name="twitter:image" content="https://tattooos.co/og-image.png" />
<!-- seo:head:end -->
```
Keep the existing comment explaining the duplication with `legalEntity.ts`.

### 9.4 — Build script

**File:** `frontend/scripts/prerender-meta.ts` (Node 24 type stripping — erasable syntax only; import the
renderer and manifest by relative path **with** the `.ts` extension). For each entry in `STATIC_ROUTES`
it reads `dist/index.html`, renders the route, and writes `dist/<path>.html` (creating parent directories,
e.g. `dist/use/booking.html`). It exits non-zero if any file was not written. Take the `dist` directory as
an optional argument so a unit test can point it at a temp directory.

**`frontend/package.json`:** `"build": "tsc -b && vite build && node scripts/prerender-meta.ts"`. If
`tsc -b` picks up `scripts/`, make sure it type-checks; if Node prints an experimental-type-stripping
warning on the CI/Docker Node version, pass the flag documented for that version rather than suppressing.

### 9.5 — nginx: serve the per-route file

In `location /` change the fallback to try the prerendered file before the SPA shell:

```nginx
        try_files $uri $uri.html $uri/ /index.html;
```
`/pricing` → `pricing.html`, `/use/booking` → `use/booking.html`; `/s/anything` and the private routes
find no `.html` file and fall through to `/index.html` exactly as before. Because these are files, not
directories, nginx never issues a directory redirect (which would put `:8080` in the `Location` header).
`Cache-Control: no-cache` is already set on this location, so a deploy is picked up immediately.

---

## 10. Phase 5 — Crawler HTML shell for `/s/:slug` and `/artist/:slug` (SEPARATE PR, D1)

Do this only after Phases 1–4, 6 and 7 are merged and the Phase 7 smoke test is green in CI. Metadata for
studio/artist pages depends on per-request data, so it cannot be built at build time. Link-preview bots
(Instagram/Facebook/WhatsApp/Slack/X/iMessage) and some crawlers read only the raw HTML — and studios
share exactly these links from their Instagram bios.

**API — two new anonymous endpoints** in `PublicEndpoints.cs` (each `AllowAnonymous`, `public-read`
rate limit, `Cache-Control: public, max-age=300`, `Content-Type: text/html; charset=utf-8`):
`GET /api/v1/public/seo/studios/{slug}` and `GET /api/v1/public/seo/artists/{slug}`. Each sends the
existing `GetPublicStudioQuery` / `GetPublicArtistQuery` (no new data access) and renders a **minimal HTML
document**: `<title>` and description (same strings the SPA's `useDocumentMeta` builds for that page),
canonical `SITE_URL + path`, OG/Twitter tags with the cover/profile image only when it is an `https` URL,
`<meta name="robots" content="index,follow">`, a JSON-LD block equivalent to what the SPA's
`useStructuredData` emits, and a short visible body (`<h1>` name, description, city, and for a studio its
artists as links). It ends with a link to the same canonical URL. **A missing/unpublished slug returns a
real 404**, not a 200 (no soft 404s).
- **Every dynamic value is HTML-encoded** (`System.Net.WebUtility.HtmlEncode` for text/attributes;
  serialise JSON-LD with `System.Text.Json` default encoding, which escapes `<`, so a name cannot close the
  `<script>`). This is a stored-XSS surface; test it (§13).
- The shell's content must **match what a human sees on the page** (search-engine policy on dynamic
  rendering). Do not put keywords or text in the shell that the page does not show.
- Add two rows to the AllowAnonymous Exceptions table (justification: read-only, only fields the public
  API already exposes; mitigation: rate limit + encoding + 404 for unpublished).

**nginx — route crawler user agents to the shell** (all other visitors continue to the SPA):

```nginx
map $http_user_agent $seo_bot {
    default 0;
    ~*(facebookexternalhit|facebot|twitterbot|linkedinbot|whatsapp|slackbot|telegrambot|discordbot|pinterest|applebot|skypeuripreview|redditbot|googlebot|bingbot|duckduckbot|yandex|baiduspider) 1;
}
```
and in the server block, before `location /`:

```nginx
    location ~ ^/(s|artist)/[^/]+/?$ {
        if ($seo_bot) { return 418; }
        error_page 418 = @seo_shell;   # the `return 418` above (bots only) is handed to @seo_shell
        add_header Cache-Control "no-cache" always;
        add_header X-Content-Type-Options nosniff always;
        add_header X-Frame-Options DENY always;
        add_header Referrer-Policy strict-origin-when-cross-origin always;
        add_header X-Robots-Tag "${IS_STAGING}" always;
        try_files $uri $uri/ /index.html;
    }

    location @seo_shell {
        rewrite ^/s/([^/]+)/?$      /api/v1/public/seo/studios/$1 break;
        rewrite ^/artist/([^/]+)/?$ /api/v1/public/seo/artists/$1 break;
        proxy_pass http://${BACKEND_HOST}:${BACKEND_PORT};
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        add_header Vary "User-Agent" always;
        add_header X-Robots-Tag "${IS_STAGING}" always;
    }
```
This is the most delicate nginx in the change: `if`/`error_page`/`rewrite … break` inside a named
location, and a regex location that must copy the security headers `location /` sets (a location with its
own `add_header` does not inherit server-level ones — the same reason the template repeats
`X-Robots-Tag` everywhere). **It must be verified by the Phase 7 smoke test against a real nginx** with a
`User-Agent: facebookexternalhit/1.1` request returning HTML containing the studio-specific `<title>` (the
smoke test has no backend, so assert on the 502/proxy behaviour, and prove the real rendering with an
API-level unit test plus a local run of API + built frontend image if Docker is available). If the nginx
cannot be verified, **do not merge Phase 5**; report instead.

---

## 11. Phase 6 — Owner-facing links use the apex, and Help says so (D2)

1. `ArtistDetailPage.tsx:640` and `ArtistSocialTab.tsx:75`: replace `import.meta.env.VITE_PUBLIC_URL ??
   window.location.origin` with `SITE_URL`. Leave `EmbedCodeCard.tsx` / `EmbedPage.tsx` untouched.
2. **Help sync (rule 7).** Grep `frontend/src/features/help/helpContent.ts` and
   `frontend/public/user-manual/index.html` for every place an owner or artist is told where their public
   page lives or how to share it (start with the articles around lines mentioning "public page", "View
   public portfolio", the Verified-badge and social-links text, and the QR code). State that the shareable
   link is on `tattooos.co`, that the QR code points there too, and that the same page also opens under
   the app address but `tattooos.co` is the one to share. Update any onboarding tour step only if a
   `data-tour` target or nav label changed (none is expected). State "considered, no tour change" in the PR.

---

## 12. Phase 7 — CI: smoke-test the frontend image's nginx (land FIRST)

**File:** `.github/workflows/ci.yml`, `docker-build` job. Load the image instead of discarding it, then run
it and assert real behaviour (the container has no backend, so `BACKEND_HOST=127.0.0.1` with a dead port
makes upstream names resolvable; proxied paths then answer 502, which is enough to prove the proxy path):

```yaml
      - name: Build frontend image
        uses: docker/build-push-action@v7
        with:
          context: .
          file: frontend/Dockerfile
          push: false
          load: true
          tags: tattooos-frontend:ci
          # ...existing cache and build-args unchanged

      - name: Smoke-test the frontend image's nginx
        run: |
          set -euo pipefail
          docker run -d --name fe -p 8081:8080 -e BACKEND_HOST=127.0.0.1 -e BACKEND_PORT=9 tattooos-frontend:ci
          for i in $(seq 1 30); do curl -fsS -o /dev/null http://localhost:8081/ && break || sleep 1; done
          fail() { echo "SMOKE FAIL: $1"; docker logs fe | tail -30; exit 1; }
          # Capture into variables and match with here-strings: `curl | grep -q` under pipefail can
          # fail intermittently (grep exits on first match, curl gets a broken pipe).
          body()    { curl -fsS "http://localhost:8081$1"; }
          headers() { curl -sI "http://localhost:8081$1" | tr -d '\r'; }
          out=$(body /pricing);      grep -qF '<title>Pricing — TattooOS</title>' <<<"$out" || fail "prerendered /pricing title"
          out=$(body /);             grep -qF 'booking & studio management' <<<"$out"       || fail "home default title"
          out=$(headers /dashboard); grep -qi '^x-robots-tag: noindex' <<<"$out"            || fail "/dashboard not noindex"
          out=$(headers /pricing);   if grep -qi '^x-robots-tag' <<<"$out"; then fail "/pricing must not carry X-Robots-Tag"; fi
          out=$(headers /sitemap.xml); if grep -i '^content-type' <<<"$out" | grep -qi 'text/html'; then fail "/sitemap.xml is being answered by the SPA"; fi
          out=$(body /robots.txt);   grep -q '^Disallow: /api/' <<<"$out"                   || fail "robots.txt"
          docker rm -f fe
```
Add one assertion per behaviour each phase introduces (Phase 5: a `User-Agent: facebookexternalhit/1.1`
request to `/s/anything` must not return the SPA shell). If a check you add cannot pass without a backend,
say so and cover it another way rather than weakening it. Also add `nginx -t` explicitly
(`docker run --rm ... nginx -t` after envsubst, or read the container start log) so a syntax error fails
with a clear message.

---

## 13. Tests

**Backend (unit):**
- `GetSitemapUrlsHandlerTests` — marketing paths present with null `LastModified`; unpublished active studio
  excluded; published active studio included; artist of an inactive studio excluded; deleted/inactive/
  slug-less artist excluded; artist of an unpublished-but-active studio included.
- `SitemapXmlWriterTests` — well-formed XML; `&`/`<` in a path is escaped; `<lastmod>` omitted when null and
  formatted `yyyy-MM-dd` otherwise; empty list yields a valid empty `<urlset>`.
- Phase 5: shell renderer tests — encodes `<script>`/quotes/ampersands in name and description; JSON-LD
  cannot break out of its script tag; `http` cover image URLs are omitted; unknown slug → 404 result;
  unpublished studio → 404.

**Frontend (vitest):**
- `renderRouteHtml.test.ts` — replaces only the marked region; escapes attribute values; throws when a
  marker is missing; output contains exactly one `<title>`, one `description`, one canonical.
- `siteRoutes.test.ts` — paths unique and start with `/`; every title ≤ 65 chars and description 50–170
  chars; `SITE_URL` equals `legalEntity.ts`'s; `SitemapPaths.Marketing` on the backend is mirrored here
  (assert the marketing subset by a fixture list you keep in both places — comment why).
- `prerender-meta` script test — run it against a temp `dist/` containing a fixture `index.html`; assert
  `pricing.html` and `use/booking.html` exist and carry their own title; non-zero exit when a marker is
  missing.
- Page tests — each page's `document.title` equals its manifest entry; `PublicContentLayout` canonical is
  `SITE_URL + path` regardless of `window.location.origin`.
- `seoRouteCoverage.test.ts` — parses `router.tsx` and `nginx.conf.template`: every router path is either
  in a `PUBLIC_INDEXABLE` allowlist (kept in the test) or matched by the noindex map's regexes. A new route
  that is in neither fails the build with the path named.
- `useDocumentMeta` — adding the tagged static tags must not leave duplicates (render, then assert one
  `og:title` in `document.head` after the hook runs against a head seeded like `index.html`).

**CI:** the Phase 7 smoke test is the nginx test. State in the PR which assertions map to which phase.

**All new/changed suites run clean, plus the full existing frontend and backend suites.**

---

## 14. Industry-standard benchmark note (CLAUDE.md rule 6)

Verify against the vertical booking-SaaS set (Fresha, Booksy, Vagaro, GlossGenius, Mangomint) by fetching the
**raw HTML** (not the rendered DOM) of one public marketing page and one public provider page each, and
record what you find in the Decisions Log with the URLs and the date: per-page `<title>`, absolute
`og:image`, canonical, sitemap location, and whether provider pages are server-rendered. This draft did
not run that check — do not copy its assumptions; if the category norm is stricter than this prompt (for
example full SSR for provider pages), flag the gap explicitly rather than silently shipping less.

## 15. Explicitly deferred

- Full server-side rendering or a prerendered page body (only metadata is addressed here).
- JSON-LD for the marketing pages (`Organization`, `SoftwareApplication`).
- 301-redirecting marketing paths from `app.tattooos.co` to the apex (D3), and `hreflang`/localisation.
- Search Console, Analytics, and platform domain verification (to-do steps 15–19; manual).
- Real brand artwork for `og-image.png` (marketing asset); the generated placeholder stays until supplied.
- Centralising the backend's duplicate apex literals (`SiteBaseUrl` in `PublicEndpoints.cs`, `BaseUrl` in
  `GetStudioQrCodeQuery`) into one Application constant — noted, not done.
- Removing `IsPublished` skew between the sitemap and `GetNearbyStudios` beyond the fixes in §6.1.

## 16. Final self-check — run before declaring done

- [ ] `dotnet build "Pena e Arte.slnx" --configuration Release` clean; new backend tests pass; full unit
      suite green; `dotnet format` verified on touched files (CRLF).
- [ ] `cd frontend && pnpm lint && pnpm build && pnpm test` green; `pnpm build` produces `dist/pricing.html`
      and `dist/use/booking.html`, each with its own `<title>`; `dist/index.html` still has the defaults.
- [ ] `git diff --stat` shows no change to `k8s/**`, `cd.yml`, `PublicPageHeader.tsx` or `sw.js`.
- [ ] `rg "https://tattooos.co" frontend/src` — only tests, `legalEntity.ts` and `siteRoutes.ts` remain.
- [ ] The Phase 7 smoke test runs in CI and is green; state which assertions it contains.
- [ ] Manual, on a built frontend image if Docker works: `curl -I /dashboard` → noindex, `/pricing` → no
      robots header and its own title, `/sitemap.xml` → not HTML. If Docker does not work, say so and hold
      the nginx changes.
- [ ] Rendered check with a real browser (Playwright) of `/pricing`, `/faq` and a studio page: exactly one
      `og:title`, one canonical (apex), correct `document.title`.
- [ ] Final summary states plainly: raw-HTML metadata is now fixed for the static routes (and for
      studio/artist pages only if Phase 5 shipped); the page bodies are still client-rendered; Search
      Console has not been touched and the sitemap should be submitted there only after this is live.
- [ ] Final summary lists the Claude Docs to-do items to tick (steps 11–14) as a manual follow-up.

## 17. Final deliverable spec

**Suggested PR split:** PR 1 = Phases 7, 1, 2, 3, 4, 6 (in that order: smoke test first, so the nginx
changes are proven by it). PR 2 = Phase 5 (only if D1 stays "build it").

**Docs (part of the deliverable):**
- `docs/claude/architecture.md` — a Decisions Log entry `## Search Visibility — 2026-09-26` (problem,
  decisions §2, what shipped, the nginx-untested-by-CI finding and the smoke test that closes it, the
  benchmark findings from §14, and what is deferred), row #40 wording update, and — for Phase 5 — two
  AllowAnonymous rows.
- `frontend/src/features/help/helpContent.ts` and `frontend/public/user-manual/index.html` per §11.

**Commit messages:** `feat: nginx smoke test in CI`, `fix: sitemap lists only pages that render, reachable
at /sitemap.xml`, `feat: noindex private routes, robots.txt`, `feat: per-route metadata in the raw HTML for
static routes`, etc. — one focused commit per phase. Add the standard `Co-Authored-By` trailer.

**PR description should note:** this fixes raw-HTML metadata and indexing hygiene, it does not add SSR; the
nginx changes are verified by the new CI smoke test; the sitemap should be submitted to Search Console
once this is live.
