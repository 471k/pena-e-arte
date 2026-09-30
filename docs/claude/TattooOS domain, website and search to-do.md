# TattooOS domain, website and search to-do

Sep 26, 2026 · @Phi

Email comes first because every other item depends on a working company inbox. Everything below it waits until email is done.

## 1. Email (now)

`tattooos.co` has no MX record, so nothing sent to `support@tattooos.co` is received. The app shows that address to users.

- [x] Pick the provider: Google Workspace Business Starter (recommended) or Zoho Mail Lite
- [x] Create one admin user for yourself, with two-factor on and backup codes stored safely
- [x] `support@` and `social@` created as groups, owned by you, open to external senders — single-member (you) for now; add a second person when there's one to add
- [x] In Cloudflare, add the provider's MX records on the root domain
- [x] Add the provider's SPF TXT record and DKIM record
- [x] Add a DMARC record (start with `p=none`, tighten later)
- [x] Leave Resend's `send.` and `resend._domainkey` records untouched
- [x] Test: send to `support@` and `social@` from an outside Gmail; both arrive, not in spam
- [x] Set up a company password manager for the mailbox and social logins (KeePass)
- [x] Tell marketing it's done so they can create the social accounts

## 2. Domain and website

Only `app.`, `staging.` and `test.` resolve today; `tattooos.co` and `www.tattooos.co` go nowhere.

- [x] Decide where the marketing site lives: in this repo, or a site builder you edit yourself (Framer, Webflow)
- [x] Stopgap: Cloudflare redirect from `tattooos.co` and `www.` to `app.tattooos.co` — superseded by the real marketing site, not built
- [x] Build the marketing site on `tattooos.co` as ready-made pages: home, features, pricing, one page per use (booking, deposits, consent forms), FAQ
- [x] Point `www.` at the site and redirect it to `tattooos.co`

## 3. Search visibility (engineering)

The app renders every page in the browser from one HTML file, so Google and link previews see the same title everywhere. These become one overnight prompt.

- [ ] Fix `robots.txt`: it points to `https://tattooos.co/sitemap.xml`, which doesn't exist
- [ ] Generate a sitemap listing the marketing pages and every public studio (`/s/:slug`) and artist (`/artist/:slug`) page
- [ ] Give each public studio and artist page its own title, description and preview image, served in the HTML (prerendering or server-side rendering)
- [ ] Add the missing `og-image.png` that `index.html` references
- [ ] Keep logged-in app pages out of the index (`noindex`)
- [ ] Update Help content and the user manual where studio owners see their public page

## 4. Google accounts and social

All owned by the company Workspace account, not a personal Gmail.

- [ ] Google Search Console: verify `tattooos.co` with a DNS TXT record, submit the sitemap
- [ ] Google Analytics on the marketing site
- [ ] Google Business Profile, if it fits a software company with no storefront
- [ ] Meta domain verification with the DNS TXT option (marketing sends the code)
- [ ] Same check for TikTok, X or LinkedIn if they ask
- [ ] Social links in the website footer once marketing sends the handles

## 5. For Marketing, and open decisions

- [ ] Marketing project: keywords, page copy, positioning and content plan for the marketing site
- [x] Update the marketing email request doc with the answers to its questions once email is live
- [x] Decide: Google Workspace or Zoho Mail
- [x] Decide: marketing site in the repo, or a site builder
- [ ] Decide: optional public `hello@tattooos.co`, or skip for now
- [ ] Decide D1: build the crawler HTML shell for studio and artist pages (search-visibility prompt, Phase 5) as a separate second PR. Default: yes
- [ ] Decide D2: owner-facing public links use tattooos.co instead of the app address. Default: yes
- [ ] Decide D3: 301 the marketing paths from app.tattooos.co to the apex. Default: no, canonical links are enough

## 6. Rollout order (in dependency order, one by one)

Steps 1 to 3 can start now. Steps 4 to 10 ship the marketing site (prompt: `docs/claude/overnight-prompt-marketing-site-2026-09-26.md`). Everything after waits on the live site.

**Start now**

- [x] 1. Land the infra log and the marketing-site prompt as a small docs PR (not this to-do copy) — PR #187, merged
- [x] 2. Update the marketing email request doc with the answers to its questions — answered in the doc's "Engineering's answers — 2026-09-26" section
- [x] 3. Cloudflare (Phi only): add `@` and `www` records mirroring `app`, plus a 301 redirect rule from `www` to the apex that keeps the path (check the free-plan limit of 3 redirect rules first) — done 2026-09-26: A records `@` and `www` (49.13.66.15, proxied), rule `www to apex` active, `curl -I` returns 301 to the apex

**Build the site** (branch `feat/marketing-site-2026-09-26`, off `main`)

- [x] 4. Confirm `Plan` has no tenant query filter (`Persistence/AppDbContext.cs`) — confirmed, no query filter
- [x] 5. Backend: `GetPublicPlansQuery`, `GET /api/v1/public/plans`, unit tests, AllowAnonymous row in `architecture.md` — done, PR #188; route is `GET /api/v1/public/plans` (public-read rate limited)
- [x] 6. Frontend: `publicApi` hook, Features, Pricing, three use-case and FAQ pages, Home "Explore" nav row, routes, exports, tests — done, PR #189
- [x] 7. Confirm the Help-sync verdict (no update needed) against `helpContent.ts` and the manual — confirmed, no Help change needed
- [x] 8. Ingress: add the `tattooos.co` host with its own `pena-e-arte-marketing-tls` secret, then merge so CD deploys (needs step 3) — done, PR #190; certificate issued in about 2 minutes
- [x] 9. Verify from Phi's machine: `dig` both hosts, `curl -I www.tattooos.co/pricing` returns 301 to the apex, cert issued — done 2026-09-26: apex 200, `www` 301 to apex, certificate `pena-e-arte-marketing-tls` READY, `app.` unaffected
- [x] 10. Add the `architecture.md` Decisions Log entry, then tick "Build the marketing site" and "Point www." in section 2 — done in PR #190

**Search visibility** (after the site is live; needs its own overnight prompt)

- [x] 11. Sitemap and `robots.txt`: the API already has `/sitemap.xml` but nginx does not proxy it, so expose it at the apex and add the marketing pages
- [x] 12. Prerender per-page title, description and preview image for public pages, and pin the marketing pages' canonical to the apex
- [x] 13. Add the missing `og-image.png` and `noindex` on logged-in app pages
- [x] 14. Update Help and the manual where owners see their public page

Done 2026-09-27, PR #192, verified live on tattooos.co: sitemap 200 application/xml, robots.txt, noindex on private routes, per-route raw titles, apex canonicals, og-image 200. Phase 5 (crawler HTML shell for /s/:slug and /artist/:slug) also done and verified, PR #194, 2026-09-27: bot User-Agent (facebookexternalhit, Twitterbot) gets the studio/artist-specific title, apex canonical and JSON-LD; a browser User-Agent on the same URL still gets the plain SPA. Verified live on staging's real seed data (ink-and-iron-studio, elira-dervishi) since production has no published studios/artists yet.

Prompt written: `docs/claude/overnight-prompt-search-visibility-2026-09-26.md` (PR #191). Run it after confirming D1 to D3 in section 5. It leads with a CI smoke test of the frontend image's nginx, because nginx is not covered by CI today.

**Google and social** (after the site and sitemap are live; marketing sends codes and handles)

- [ ] 15. Search Console: verify by DNS TXT, submit the sitemap
- [ ] 16. Google Analytics on the marketing site
- [ ] 17. Meta domain verification (a meta tag instead of DNS would need a small `index.html` change)
- [ ] 18. Same check for TikTok, X and LinkedIn if they ask
- [ ] 19. Social links in the footer once marketing sends the handles
