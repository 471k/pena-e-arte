# Overnight Prompt — Marketing Site at the Apex Domain (`tattooos.co`)

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt. It is self-contained: exact files, exact current code,
> exact target code, exact tests, exact docs to sync. Read the whole file before writing
> anything — later sections depend on decisions made in §2 and §5. **Mode: fully autonomous**
> for every code/config/test change in this file; the DNS and Cloudflare-console steps in §6.1
> are **BLOCKING-MANUAL** (Phi only, real external credentials/console access) and must be
> confirmed done before the Ingress/cert changes in §6.2 will actually resolve — do the
> code-side work regardless (it merges and deploys cleanly either way), but do not claim the
> site is "live" without confirming §6.1 happened.

**Date logged:** 2026-09-26
**Requested by:** Phi
**Origin:** Engineering-consultation session in the separate "Pena e Artë - Engineering
Consultation" project, working section 2 ("Domain and website") of the `TattooOS domain,
website and search to-do` Claude Docs page. `tattooos.co` and `www.tattooos.co` currently
resolve to nothing — only `app.`, `staging.`, and `test.` have DNS records. Decision made in
that consultation, confirmed by Phi: **build the marketing site inside this repo** (not a
third-party site builder like Framer/Webflow), because (a) it's ordinary engineering work
well within an overnight prompt, (b) this repo already has a working public-page pattern
(`PublicContentLayout`, `useDocumentMeta`, `SiteFooter`, `PublicPageHeader`) that a from-scratch
site builder would just duplicate, and (c) Phi is currently the sole engineering-decision-maker
and already runs this exact overnight-prompt workflow, so there's no near-term win from
decoupling marketing-copy edits into a separate tool. Revisit a site builder later only if a
non-technical marketing hire needs to self-serve edit copy without going through a prompt.

**Before starting:**
```bash
git status                                 # confirm clean; note current branch below
git checkout main && git pull
git add -A && git commit -m "chore: pre-marketing-site checkpoint" --allow-empty
git checkout -b feat/marketing-site-2026-09-26
```
**Repo state at the time this prompt was written:** the working tree was on
`fix/signalr-multi-replica` (already merged/up to date with its own remote), with three
untracked docs files sitting uncommitted (`docs/claude/infra-log-email-domain-setup-
2026-09-26.md`, a stray local `docs/claude/TattooOS domain, website and search to-do.md`,
plus local `.claude/settings.local.json` / `frontend/.claude/` tooling files). **Branch off
`main`, not off `fix/signalr-multi-replica`** — this prompt has no dependency on that branch.
If `docs/claude/infra-log-email-domain-setup-2026-09-26.md` is still untracked when you start,
commit it for real (it's a real doc, already correct, just never landed) as its own small
first commit before branching for this feature — don't fold an unrelated doc into this
feature's commit history. Leave the stray duplicate `TattooOS domain, website and search to-do.md`
file alone — it's not this repo's canonical copy (that lives in Claude Docs) and deleting or
touching it is out of scope for this prompt.

---

## 1. What's already there (verified against live source, 2026-09-26 — do not re-derive)

**Deploy architecture is self-hosted Kubernetes (K3s on a Hetzner box), not Cloudflare Pages
or any static-hosting service.** `frontend/Dockerfile` builds the Vite SPA with `pnpm build`
(pnpm@11.5.1, pinned) and serves the `dist/` output from an `nginxinc/nginx-unprivileged`
container (`frontend/nginx.conf.template`). `.github/workflows/cd.yml` builds and pushes that
image to GHCR, then a self-hosted runner `kubectl apply`s `k8s/overlays/production` (Traefik
Ingress + cert-manager, DNS-01 challenges against Cloudflare). Today, `k8s/base/ingress.yaml`
has exactly one host: `app.tattooos.co`, routed to the single `tattooos-frontend` Service (2
replicas). **Whatever ships here rides the existing CD pipeline — merging to `main` after CI
passes is what deploys it. No manual `kubectl` step is needed for the code changes in this
prompt**, only for the DNS/Cloudflare console work in §6.1, which nothing in this repo can do
for you.

**The SPA already has a real public-page system — this prompt extends it, it does not
replace it or build a parallel one:**

- `frontend/src/features/public/components/PublicContentLayout.tsx` — the shared shell every
  public content page uses: renders `PublicPageHeader` + a centered `max-w-3xl` content column
  + `SiteFooter`, and calls `useDocumentMeta({ title, description, canonical })` so each page
  gets a distinct client-side `document.title`/meta-description/canonical link.
- `frontend/src/features/public/components/HomePage.tsx` — already the public landing surface
  for unauthenticated visits to `/` (wired in `router.tsx`'s `IndexRedirect`, added under
  PENA-102). Uses `PublicContentLayout` with `title={SITE_TAGLINE}` / `description=
  {SITE_META_DESCRIPTION}` from `frontend/src/shared/constants/legalEntity.ts`. Currently just
  a headline, two CTAs ("Discover studios" → `/discover`, "Register your studio" → `/register`),
  and a policy-link nav.
- `frontend/src/features/public/components/PrivacyPolicyPage.tsx` / `TermsOfServicePage.tsx` /
  `RefundPolicyPage.tsx` / `ContactPage.tsx` — same `PublicContentLayout` pattern, mounted as
  top-level routes in `router.tsx` **outside** the authenticated `AppRoot` tree (`/privacy`,
  `/terms`, `/refund-policy`, `/contact`) — i.e. reachable with zero auth, zero tenant context.
- `frontend/src/features/public/components/PublicPageHeader.tsx` — sticky header, brand mark +
  "Discover" link + (logged-out) Sign in/Sign up/Register studio, or (logged-in) an
  `AuthenticatedNav` account dropdown. Already used by `PublicContentLayout`, so every page
  built on that layout gets it for free.
- `frontend/src/shared/components/SiteFooter.tsx` — site-wide footer with the four policy
  links + `© {year} TattooOS`. Rendered on every `PublicContentLayout` page already.
- `frontend/src/shared/utils/useDocumentMeta.ts` — sets `document.title` + `<meta
  name="description">` + canonical `<link>` client-side on mount, restores the previous title
  on unmount. This is a real, working, existing SEO primitive — but it only mutates the DOM
  after the SPA's JS executes; it does **not** change what's in the raw HTML response, which
  is why `frontend/index.html` still ships one static shared `<title>`/`<meta>` for every
  route (see the comment at the top of `index.html`: `legalEntity.ts` is source of truth,
  duplicated there because there's no Vite templating plugin to import a TS constant into
  static HTML). **Fixing that raw-HTML gap (prerendering) is explicitly out of scope for this
  prompt — see §14.**

**What does NOT exist yet:** no `/features`, `/pricing`, `/faq`, or per-use-case
(booking/deposits/consent-forms) route. No public, unauthenticated way to read the Plan
catalog — `GET /api/v1/billing/plans` is `RequireAuthorization("OwnerOnly")`
(`Pena_e_Arte.API/Endpoints/BillingEndpoints.cs:26`). `frontend/public/robots.txt` still points
at a `sitemap.xml` that doesn't exist, and `og-image.png` referenced by `index.html` is missing
— both are section 3 of the to-do doc, not this prompt (see §14).

**Nginx routing (`frontend/nginx.conf.template`) already handles same-origin `/api/` and
`/hubs/` proxying, per-location caching/security headers, and an `X-Robots-Tag` header driven
by an `IS_STAGING` env var (empty string on production ⇒ header omitted; a real value on
staging ⇒ `noindex`). Nothing about this needs to change for the marketing pages — they're
served by the exact same SPA build, same container, same `location /` SPA-fallback block.**

---

## 2. Decisions already made — implement as specified, do not re-litigate

1. **One SPA, one deployment, two (soon three) hostnames.** `tattooos.co` (new) and
   `app.tattooos.co` (existing) point at the **same** `tattooos-frontend` Kubernetes Service —
   not a second Deployment, not a separate Docker image, not a second build pipeline. The
   marketing pages are additional React Router routes in the same bundle, exactly like
   `/privacy` and `/contact` already are.
2. **`www.tattooos.co` redirects to `tattooos.co` at the Cloudflare edge**, not via the
   Kubernetes Ingress/nginx. It never needs to reach the origin — see §6.1. This matches
   the to-do doc's own phrasing ("Point `www.` at the site and redirect it to `tattooos.co`")
   and is simpler than adding a third Ingress host + nginx rewrite rule for a redirect that
   Cloudflare already does natively and for free.
3. **The to-do doc's own "stopgap: Cloudflare redirect from `tattooos.co`/`www.` to
   `app.tattooos.co`" item is superseded, not implemented.** That stopgap existed only because
   nobody had built the real site yet. This prompt ships the real site directly — mark that
   stopgap item done-by-supersession in the final docs update (§16), don't build a redirect
   that immediately gets torn down in the same prompt.
4. **Pricing data comes from a new public, read-only endpoint — never hardcoded copy.** A
   Pricing page with numbers that silently drift from the real `Plan` table (whenever pricing
   changes via `PlanEditPage`) is a worse outcome than a small new `AllowAnonymous` endpoint.
   See §7 for the exact shape, and §7's own justification entry for the `AllowAnonymous`
   Exceptions table (CLAUDE.md rule 2).
5. **New marketing pages use `PublicContentLayout`, full stop.** No new header, no new footer,
   no new page shell. This keeps the marketing pages visually and structurally consistent with
   `/privacy`/`/contact`/Home with zero new shared-component work.
6. **Scope for this pass, per the to-do doc's own item 3 wording ("home, features, pricing,
   one page per use — booking, deposits, consent forms —, FAQ"):**
   - `/` (Home) — **update**, not new (already exists).
   - `/features` — new.
   - `/pricing` — new, backed by the new public Plans endpoint (§7).
   - `/use/booking`, `/use/deposits`, `/use/consent-forms` — new, one per named use case.
   - `/faq` — new.
   Do not invent additional marketing pages beyond this list (no `/about`, no `/blog`, no
   `/customers`) — those aren't in the to-do doc and would be scope creep; flag them in the
   final summary as possible future additions instead of building them speculatively.
7. **Raw-HTML per-page `<title>`/meta/OG tags (prerendering), the sitemap, `robots.txt` fix,
   and `og-image.png` are explicitly deferred to a separate, already-anticipated overnight
   prompt (to-do doc section 3, "Search visibility").** This prompt's new pages get the same
   client-side `useDocumentMeta` treatment every other public page already has — consistent
   with existing convention, not a regression — but do **not** attempt prerendering, SSR, or
   a build-time static-HTML-snapshot step here. See §14 for exactly why and what's left for
   that follow-up prompt to pick up.

---

## 3. Scope boundary — do not touch

- `k8s/base/frontend-deployment.yaml`, `k8s/base/frontend-service.yaml`,
  `frontend/Dockerfile`, `frontend/nginx.conf.template` — **no changes.** One SPA, one image,
  one Service, per §2.1. The only Kubernetes file this prompt touches is `k8s/base/
  ingress.yaml` (§6.2).
- `.github/workflows/cd.yml` — **no changes.** The existing `build-and-push` /`deploy` jobs
  already build and roll out the frontend image on every merge to `main`; a new Ingress host
  needs no new CD job.
- Anything under `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs` other than adding the one new
  `MapGet` described in §7 — do not touch `GetPlans`, `CreatePlan`, `UpdatePlan`, `DeletePlan`,
  or any authenticated billing endpoint.
- `PlanManagementPage.tsx` / `PlanEditPage.tsx` (admin plan management) — read from, do not
  modify. The new public endpoint is a separate, additive read path.
- `frontend/public/robots.txt`, `frontend/index.html`'s `<title>`/`<meta>` block,
  `frontend/public/og-image.png` — section 3's job, not this prompt's. Do not "helpfully" fix
  `robots.txt` while you're in the area; that prompt needs to land the sitemap it points at in
  the same change, and doing it piecemeal here just creates a half-fixed state to re-diff later.
- Studio/artist public portfolio pages (`StudioPortfolioPage.tsx`, `ArtistPortfolioPage.tsx`)
  — unrelated to this prompt, do not touch.
- Do not add a new npm/pnpm package. Everything in this prompt (routing, layout, data
  fetching, forms if any) is achievable with what's already a dependency
  (`react-router-dom`, `@reduxjs/toolkit`/RTK Query, existing shared UI components under
  `frontend/src/shared/components/ui/`). If you find yourself reaching for a new package
  (an icon set variant, a carousel library, anything), stop and flag it instead of adding it.

---

## 4. Constraints (restated, apply throughout)

- TypeScript strict mode, no `any`, explicit types everywhere new code is added.
- No `useEffect` for data fetching — RTK Query only (`publicApi.ts` pattern, already
  established: `useGetPublicStudioQuery` etc.).
- No business logic in the new backend endpoint beyond a MediatR query handler; validation
  (none needed here — the endpoint takes no input) stays consistent with existing
  FluentValidation conventions if any input is ever added later.
- Tenant isolation: the new Plans endpoint reads platform-wide `Plan` rows (not tenant data)
  — confirm `Plan` has no tenant-scoped EF Core global query filter before assuming this is
  safe to exposed `AllowAnonymous` (check `Pena_e_Arte.Infrastructure/Data/
  ApplicationDbContext.cs`'s query-filter configuration for `Plan` before writing the handler
  — if `Plan` unexpectedly does have a tenant filter, stop and flag it, don't work around it).
- Every new `AllowAnonymous` endpoint must be added to `docs/claude/architecture.md`'s
  "AllowAnonymous Exceptions" table with its specific mitigation (rule 2) — see §7.4.
- Never log PII — the new endpoint touches no PII at all (Plan rows only), so this is a
  non-issue here, but do not add any ad-hoc logging that captures request IPs/emails while
  you're in the area.
- Structured logs only (Serilog) — no `Console.WriteLine`/`console.log` anywhere touched.
- Every code change ships with tests in the same commit (§13).
- Help-sync (§11) is part of the definition of done for every phase below, not an appendix.
- Every phase gets an industry-benchmark note (§12) — do not silently ship UI/structure that
  falls behind the category standard without flagging it.

---

## 5. Architecture decision, spelled out (context for why §6–§10 look the way they do)

Two live options were weighed in the consultation this prompt comes from:

**A. Add `tattooos.co` as another Ingress host on the existing SPA (chosen).** New pages are
React Router routes reusing `PublicContentLayout`. Zero new infrastructure surface beyond one
Ingress host + TLS entry. Consistent look-and-feel with the rest of the public site for free.
Cost: the raw-HTML SEO gap (client-side-only meta) isn't fixed by this alone — but it isn't
made worse either, and it's explicitly section 3's job, already anticipated as a separate
prompt before this consultation ever started.

**B. A wholly separate static build (e.g. Astro) in the same repo, deployed as its own
Docker image/Deployment/Ingress host, sidestepping the SPA's rendering model entirely for
just the marketing pages.** This *would* solve the raw-HTML SEO gap immediately, at the cost
of: a second design system (or duplicating `PublicContentLayout`/`PublicPageHeader`/
`SiteFooter`'s look by hand in a different templating language), a second build/deploy
pipeline in `cd.yml`, a second Docker image, a second Ingress host + cert, and ongoing
duplication risk between two "TattooOS public page" implementations drifting apart over time
(fonts, colors, footer links, nav — every one of these already exists once, correctly, in the
SPA).

**Chose A.** The reasoning: this repo already solved "public unauthenticated page with correct
per-page title/description, shared header/footer, matches app styling" — four times over
(`/`, `/privacy`, `/terms`, `/refund-policy`, `/contact`). Building a second, parallel solution
to the same problem in a different stack, in the same repo, on the same night pricing/features
copy is still being figured out, is the kind of duplication this project's own conventions
warn against. The one real gap — raw-HTML metadata for crawlers/link previews — applies
identically to the *existing* four public pages today and is already scoped as its own
follow-up prompt; solving it once, for all public routes at once (existing four + this
prompt's five new ones + the two dynamic portfolio routes), in a dedicated prompt, is more
coherent than solving it twice (once here with a different mechanism, once again in section 3
for the routes this prompt doesn't touch).

---

## 6. Phase 1 — DNS + Ingress cutover

### 6.1 — BLOCKING-MANUAL (Phi only — Cloudflare console)

**Do this before or in parallel with the code changes below — the code merges and deploys
cleanly either way, but the hostname won't actually resolve/serve until this is done.**

1. Open Cloudflare → DNS → Records for `tattooos.co`. Note the exact record **type**
   (A or CNAME), **target/content**, and **proxy status** (orange "Proxied" vs grey "DNS
   only") of the existing `app` record. Confirmed against the real dashboard, Phase 0 step 4
   of the original production-deploy prompt: `app.tattooos.co`'s record is what cert-manager's
   DNS-01 challenge (via the `cloudflare-api-token` Secret, same one used for `letsencrypt-
   prod-dns01`) has successfully issued a cert against before — mirror it exactly for the new
   records rather than guessing a value.
2. Add a new record: **Name:** `@` (apex/root), **same type and target** as `app`'s record,
   **same proxy status**. This is `tattooos.co` itself.
3. Add a second new record: **Name:** `www`, same type/target/proxy status as step 2 (it never
   needs to resolve to anything meaningful on its own — it only needs to be proxied through
   Cloudflare so the Redirect Rule in step 4 can fire before any request reaches the origin).
4. Cloudflare → Rules → Redirect Rules → create a new rule:
   - **When incoming requests match:** Hostname equals `www.tattooos.co`
   - **Then:** Dynamic redirect, expression `concat("https://tattooos.co", http.request.uri.path)`
     (preserves the path — `www.tattooos.co/pricing` → `https://tattooos.co/pricing`, not a
     blanket redirect to the apex only), **Status code 301**, **Preserve query string: on**.
   - This is a free-tier Cloudflare feature (up to 3 Redirect Rules on Free); confirm the zone
     hasn't already used all 3 before assuming this will save cleanly — if it has, report which
     existing rules are consuming the quota and stop rather than deleting one without asking.
5. **Do not** create a page rule or DNS record that points `www` at `app.tattooos.co` directly
   — that would serve the authenticated app shell at `www.`, not a redirect to the marketing
   site, and is a different (wrong) behavior from what's specified here.
6. Confirm (this session or Phi, whichever runs last): `dig tattooos.co` and `dig
   www.tattooos.co` both resolve, and `curl -sI https://www.tattooos.co/pricing` returns a
   `301` with `location: https://tattooos.co/pricing`. This environment's own DNS tools were
   unreliable during the earlier email-setup work in this same consultation (network-
   unreachable errors from both the cloud container and the remote-devices shell) — if that's
   still true, this check has to run from Phi's own machine/browser, not assumed from a failed
   tool call.

### 6.2 — Ingress + TLS (code change)

**File:** `k8s/base/ingress.yaml`

Current content (verbatim, confirmed 2026-09-26):
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: tattooos
  namespace: tattooos
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod-dns01
spec:
  ingressClassName: traefik
  tls:
    - hosts: ["app.tattooos.co"] # confirmed against the real Cloudflare DNS record, Phase 0 step 4
      # Deliberately kept as the old name — Secrets don't move between namespaces so a fresh
      # cert reissues in the new namespace regardless of what this string says; renaming it too
      # would just be unnecessary churn on an internal-only, never-user-visible identifier.
      secretName: pena-e-arte-tls
  rules:
    - host: app.tattooos.co
      http:
        paths:
          # frontend nginx is already a same-origin reverse proxy for /api/ and /hubs/
          # (frontend/nginx.conf.template) — no second Ingress host needed.
          - path: /
            pathType: Prefix
            backend:
              service:
                name: tattooos-frontend
                port: { number: 8080 }
```

**Target content** — add `tattooos.co` as its own `tls` entry with its **own** `secretName`
(not folded into `pena-e-arte-tls`'s existing SAN list) so a cert-issuance hiccup for the new
apex domain (e.g. DNS not propagated yet when cert-manager first tries) can never block or
retrigger reissuance of the already-working `app.tattooos.co` cert, and add a second `rules`
entry routing it to the same `tattooos-frontend` Service. **`www.tattooos.co` gets no Ingress
host at all** — per §2.2/§6.1, it's redirected at the Cloudflare edge and never reaches this
cluster:

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: tattooos
  namespace: tattooos
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod-dns01
spec:
  ingressClassName: traefik
  tls:
    - hosts: ["app.tattooos.co"] # confirmed against the real Cloudflare DNS record, Phase 0 step 4
      # Deliberately kept as the old name — Secrets don't move between namespaces so a fresh
      # cert reissues in the new namespace regardless of what this string says; renaming it too
      # would just be unnecessary churn on an internal-only, never-user-visible identifier.
      secretName: pena-e-arte-tls
    - hosts: ["tattooos.co"] # marketing site at the apex domain — 2026-09-26. Own secretName
      # (not appended to pena-e-arte-tls's hosts list) so a first-issuance failure here (e.g.
      # DNS not yet propagated per §6.1) can never block or force reissuance of the
      # already-working app.tattooos.co cert above.
      secretName: pena-e-arte-marketing-tls
  rules:
    - host: app.tattooos.co
      http:
        paths:
          # frontend nginx is already a same-origin reverse proxy for /api/ and /hubs/
          # (frontend/nginx.conf.template) — no second Ingress host needed.
          - path: /
            pathType: Prefix
            backend:
              service:
                name: tattooos-frontend
                port: { number: 8080 }
    - host: tattooos.co
      http:
        paths:
          # Same Service, same Deployment, same build as app.tattooos.co — the marketing
          # pages are ordinary React Router routes in the one SPA (see docs/claude/
          # architecture.md's Decisions Log entry for this date for the reasoning). No
          # second Deployment/Service/image; www.tattooos.co redirects at the Cloudflare
          # edge (see §6.1) and never has its own host entry here.
          - path: /
            pathType: Prefix
            backend:
              service:
                name: tattooos-frontend
                port: { number: 8080 }
```

**After merge:** cert-manager will attempt to issue `pena-e-arte-marketing-tls` automatically
via the existing `letsencrypt-prod-dns01` ClusterIssuer (same Cloudflare API token, same DNS-01
mechanism already proven against `app.tattooos.co`) — no manual cert step needed **provided
§6.1's DNS record already exists** at the time this deploys. If it deploys before §6.1 is
done, cert-manager will simply retry until the DNS record appears (it doesn't fail
permanently on a transient DNS-01 lookup miss) — but verify this in §15 rather than assuming
it self-healed.

---

## 7. Phase 2 — Backend: public Plans endpoint

### 7.1 — Verify `Plan` has no tenant scoping before writing anything

**File to check first:** `Pena_e_Arte.Infrastructure/Data/ApplicationDbContext.cs` (its
`OnModelCreating`/global-query-filter configuration). `Plan` is a platform-wide catalog entity
(subscription tiers TattooOS itself sells to studios), not tenant data — confirm this is
still true in the live model (no `HasQueryFilter` applied to `Plan`) before treating it as
safe to read without a tenant/auth context. If it unexpectedly does have tenant scoping, stop
and flag it — that would mean the existing `OwnerOnly` `GetPlans` handler is already doing
something more subtle than a flat catalog read, and this prompt's assumption is wrong.

### 7.2 — New query + handler

**File:** `Pena_e_Arte.Application/Billing/Queries/GetPublicPlansQuery.cs` (new)

Follow the exact shape of the existing `GetPlansQuery`/`GetPlansHandler` pair (read them first
— they live alongside `BillingEndpoints.cs`'s handler references) but with a response DTO
that **excludes** anything not meant for an anonymous visitor: no Stripe Price/Product IDs, no
internal `IsActive`/soft-delete flags beyond filtering by them server-side, no admin-only
fields (e.g. whatever `PlanEditPage.tsx` exposes that a public visitor shouldn't see — check
that page's fields before assuming the existing `PlanResponse` DTO is safe to reuse as-is; if
it isn't, define a new, smaller `PublicPlanResponse` rather than trimming fields client-side).

Target shape (adjust field names to match whatever the real `Plan` entity's public-safe
columns turn out to be, confirmed by actually reading `Plan.cs` — do not guess field names):

```csharp
public sealed record PublicPlanResponse(
    string Name,
    string Interval,        // "Monthly" / "Yearly" — matches PlanTiersYearly's existing enum/string, verify against that prompt's landed shape
    decimal Price,
    string Currency,
    IReadOnlyList<string> FeatureHighlights   // short marketing bullet list — see 7.3 on where this comes from
);

public sealed record GetPublicPlansQuery : IRequest<IReadOnlyList<PublicPlanResponse>>;
```

### 7.3 — Where do `FeatureHighlights` come from?

Check whether `Plan` already has a features/highlights column (used anywhere in
`PlanEditPage.tsx` or `SubscribePage.tsx`'s existing plan-comparison UI) before adding a new
one. If a suitable column/JSON field already exists, reuse it. If it doesn't, **do not add a
new DB column for this** — that's real schema surface for what is fundamentally marketing
copy, and marketing copy for the Pricing page is explicitly item 1 of to-do section 5 ("For
Marketing, and open decisions" — "Marketing project: keywords, page copy, positioning and
content plan"), not yet written. Instead: hardcode a short, honest, feature-derived bullet
list per plan tier **in the frontend component** (§8.3), sourced from what each tier actually
unlocks per `PlanEditPage.tsx`'s real feature-gating logic (not invented marketing language) —
and leave a clearly marked `// TODO(marketing): replace with approved copy once the marketing
project (to-do section 5) delivers it` comment at the exact line. This keeps the *numbers*
live (never drift from real pricing) while being honest that the *copy* is a placeholder
pending Marketing's actual input — which is materially different from this project's "do not
build blind" precedent (this isn't a business decision being silently made, it's an explicitly
temporary placeholder with the real, correct prices).

### 7.4 — Endpoint registration + AllowAnonymous justification

**File:** `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs`

Add, in the same group but before the authenticated plan routes for readability:
```csharp
billingGroup.MapGet("/plans/public", GetPublicPlans).AllowAnonymous();
```
(`/api/v1/billing/plans/public` — deliberately not reusing the bare `/plans` path so there is
never ambiguity in logs/traces between the authenticated admin-facing list and the public
marketing one.)

**File:** `docs/claude/architecture.md` — add a new row to the "AllowAnonymous Exceptions"
table (the same table §"1. What's already there" quoted from, currently ending at row 20):

```markdown
| `GET /api/v1/billing/plans/public` | Public marketing Pricing page (`/pricing`) needs live plan pricing without forcing a login. Returns only `Name`/`Interval`/`Price`/`Currency`/`FeatureHighlights` — no Stripe IDs, no internal flags, no tenant data. `Plan` is a platform-wide catalog entity with no tenant query filter (confirmed §7.1 of the marketing-site overnight prompt). |
```
Match the table's exact existing column structure — read the live table first, don't assume
it only has the columns shown in the excerpt above.

### 7.5 — Backend tests

**File:** `tests/Pena_e_Arte.UnitTests/Billing/GetPublicPlansHandlerTests.cs` (new)
- Returns all active plans, sorted by price ascending (or whatever the existing `GetPlans`
  sort order is — mirror it for consistency, confirm by reading `GetPlansHandler`).
- Excludes inactive/archived plans (if `Plan` has an `IsActive` or equivalent flag).
- Response DTO contains no Stripe Price/Product ID field (assert the DTO type itself doesn't
  expose it, not just that a test happens not to check for it).
- Empty plan catalog → returns an empty list, not a 404/exception (a Pricing page with an
  empty catalog should render its own "no plans configured" empty state — see §8.3 — not
  error).

No new integration test is required if `GetPlansHandlerTests`' existing integration coverage
pattern already exercises the query pipeline end-to-end for `GetPlans` — mirror whatever that
file does for `GetPublicPlans` if it does.

---

## 8. Phase 3 — Frontend: new marketing pages

### 8.1 — `publicApi.ts` — new RTK Query hook

**File:** `frontend/src/features/public/publicApi.ts`

Add, alongside the existing `useGetPublicStudioQuery` etc.:
```ts
export interface PublicPlanResponse {
  name: string;
  interval: string;
  price: number;
  currency: string;
  featureHighlights: string[];
}

// ...inside the existing publicApi.injectEndpoints({...}) block, alongside the other
// public queries — follow the exact builder.query pattern already used there:
getPublicPlans: builder.query<PublicPlanResponse[], void>({
  query: () => "/billing/plans/public",
}),
```
Export `useGetPublicPlansQuery` from the same `export const { ... } = publicApi` line the
other hooks already come from, and re-export it (plus the `PublicPlanResponse` type) from
`frontend/src/features/public/index.ts` next to the existing public-API exports.

### 8.2 — `/features` page

**File:** `frontend/src/features/public/components/FeaturesPage.tsx` (new)

```tsx
import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";

// Marketing "Features" page — /features. Content sourced from the app's real, shipped
// feature set (Feature Module Map, docs/claude/architecture.md) — every bullet below
// corresponds to a feature that actually exists, no aspirational copy.
const FEATURE_GROUPS: ReadonlyArray<{ title: string; description: string }> = [
  {
    title: "Booking & deposits",
    description:
      "Clients book online, pay a deposit through Stripe, and get automatic confirmation — no more DMs and manual calendar juggling.",
  },
  {
    title: "Digital consent forms",
    description:
      "Intake and consent forms are signed digitally before the appointment, timestamped and stored — no paper, no filing cabinet.",
  },
  {
    title: "Design approval workflow",
    description:
      "Artists upload design revisions, clients approve or request changes, all in one thread tied to the appointment.",
  },
  {
    title: "Client profiles & tattoo history",
    description:
      "Every client's full history, body map, and past work lives in one place — searchable by artist or by client.",
  },
  {
    title: "Automated reminders & notifications",
    description:
      "SMS and email reminders go out automatically, cutting no-shows without anyone manually texting clients.",
  },
  {
    title: "Studio & artist public pages",
    description:
      "Every studio and artist gets a shareable public page with portfolio, reviews, and a direct booking link.",
  },
];

export function FeaturesPage() {
  return (
    <PublicContentLayout
      title="Features — TattooOS"
      description="Booking, deposits, digital consent forms, design approvals, and client records — everything a tattoo studio needs, built in."
      canonicalPath="/features"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Features</h1>
      <p className="mt-3 text-muted-foreground">
        Everything below is a real, shipped feature — not a roadmap.
      </p>

      <div className="mt-8 grid gap-6 sm:grid-cols-2">
        {FEATURE_GROUPS.map((f) => (
          <div key={f.title} className="rounded-lg border p-5">
            <h2 className="font-semibold">{f.title}</h2>
            <p className="mt-2 text-sm text-muted-foreground">{f.description}</p>
          </div>
        ))}
      </div>

      <div className="mt-10 flex flex-wrap gap-3">
        <Link
          to="/pricing"
          className="rounded-md bg-violet-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-violet-700"
        >
          See pricing
        </Link>
        <Link
          to="/register"
          className="rounded-md border-2 border-violet-500 bg-violet-500/5 px-4 py-2 text-sm font-medium text-violet-700 dark:text-violet-400 transition-colors hover:bg-violet-500/15"
        >
          Register your studio
        </Link>
      </div>
    </PublicContentLayout>
  );
}
```

**Verify the six `FEATURE_GROUPS` bullets above against `docs/claude/architecture.md`'s live
Feature Module Map before shipping them verbatim** — this prompt was written against the map
as of 2026-09-26 (features 01–20+); if the map has moved on by the time this prompt runs,
update the bullets to match what's actually shipped, per this project's own standing rule that
the map can lag reality and the live source wins.

### 8.3 — `/pricing` page

**File:** `frontend/src/features/public/components/PricingPage.tsx` (new)

```tsx
import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetPublicPlansQuery } from "../publicApi";

export function PricingPage() {
  const { data: plans, isLoading, isError } = useGetPublicPlansQuery();

  return (
    <PublicContentLayout
      title="Pricing — TattooOS"
      description="Simple, transparent pricing for tattoo studios. No commission on your bookings."
      canonicalPath="/pricing"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Pricing</h1>
      <p className="mt-3 text-muted-foreground">
        Pick a plan. No commission on your bookings, ever.
      </p>

      {isLoading && (
        <div className="mt-8 grid gap-6 sm:grid-cols-2" aria-label="Loading pricing" aria-busy="true">
          <Skeleton className="h-64 rounded-lg" />
          <Skeleton className="h-64 rounded-lg" />
        </div>
      )}

      {isError && (
        <p className="mt-8 text-sm text-muted-foreground">
          Pricing is temporarily unavailable — please{" "}
          <Link to="/contact" className="underline underline-offset-2">contact us</Link> or try
          again shortly.
        </p>
      )}

      {!isLoading && !isError && plans && plans.length === 0 && (
        <p className="mt-8 text-sm text-muted-foreground">
          Pricing is being finalized — <Link to="/contact" className="underline underline-offset-2">reach out</Link> for current rates.
        </p>
      )}

      {!isLoading && !isError && plans && plans.length > 0 && (
        <div className="mt-8 grid gap-6 sm:grid-cols-2">
          {plans.map((plan) => (
            <div key={plan.name} className="rounded-lg border p-6">
              <h2 className="font-semibold text-lg">{plan.name}</h2>
              <p className="mt-1 text-2xl font-semibold">
                {new Intl.NumberFormat(undefined, { style: "currency", currency: plan.currency }).format(plan.price)}
                <span className="text-sm font-normal text-muted-foreground">
                  {" "}/ {plan.interval.toLowerCase()}
                </span>
              </p>
              <ul className="mt-4 space-y-1.5 text-sm text-muted-foreground">
                {plan.featureHighlights.map((f) => (
                  <li key={f}>• {f}</li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      <div className="mt-10">
        <Link
          to="/register"
          className="rounded-md bg-violet-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-violet-700"
        >
          Register your studio
        </Link>
      </div>
    </PublicContentLayout>
  );
}
```

Loading / error / empty states are explicit and required (this project's own standing
proactive-recommendation: every async UI needs all three) — do not ship a version that only
handles the happy path.

### 8.4 — Use-case pages: `/use/booking`, `/use/deposits`, `/use/consent-forms`

**Files:** `frontend/src/features/public/components/UseCaseBookingPage.tsx`,
`UseCaseDepositsPage.tsx`, `UseCaseConsentFormsPage.tsx` (new — three separate files, not one
parameterized component; the to-do doc asks for "one page per use," and separate files keep
each page's copy independently editable without a shared-content indirection layer that
would just get in the way of the Marketing project's eventual copy pass).

Same `PublicContentLayout` pattern as `FeaturesPage`. Each page: one clear H1 naming the use
case, 2-3 paragraphs of honest description of the real feature (booking+deposits flow /
digital consent forms / — reuse the same source-of-truth Feature Module Map entries as §8.2),
a screenshot-shaped placeholder is **not** required (no image asset exists yet — do not invent
one or reference a path that doesn't exist), and a closing CTA row identical in structure to
`FeaturesPage`'s (`/pricing` + `/register`). Canonical paths: `/use/booking`,
`/use/deposits`, `/use/consent-forms`.

### 8.5 — `/faq` page

**File:** `frontend/src/features/public/components/FaqPage.tsx` (new)

Use the existing `Accordion` primitive already in the design system
(`@radix-ui/react-accordion`, already a dependency — `frontend/src/shared/components/ui/` —
confirm the exact export name/path before importing, this repo wraps Radix primitives under
`shared/components/ui/`, don't import `@radix-ui/react-accordion` directly in a page
component if a wrapped `Accordion` component already exists, matching every other page's
convention of using the wrapped primitives, not raw Radix).

Seed with 5-6 real, non-speculative FAQ entries answerable from what's actually true today:
what TattooOS is, whether there's commission on bookings (no — zero-commission, per ADR-0001
Amendment B, confirmed live in `docs/claude/architecture.md`'s feature 05 entry), whether
clients need an account to book (no — guest checkout exists, confirmed live per feature 01's
"guest checkout" note), how consent forms work, how to get started (register studio → link).
Do not invent FAQ answers about anything not already verified true in this repo — if a
plausible-sounding FAQ question can't be answered from verified live source, leave it out
rather than guessing.

### 8.6 — Update `HomePage.tsx` to link to the new pages

**File:** `frontend/src/features/public/components/HomePage.tsx`

Current content (verbatim, quoted in full in §1) has two CTAs and a policy-links nav only —
no link to Features/Pricing/FAQ at all, which is now a real gap once those pages exist. Add a
second nav row above the existing `<nav aria-label="Policies">` block:

```tsx
      <nav aria-label="Explore" className="mt-8 flex flex-wrap gap-x-4 gap-y-2 text-sm">
        <Link to="/features" className="underline underline-offset-2 hover:text-foreground">
          Features
        </Link>
        <Link to="/pricing" className="underline underline-offset-2 hover:text-foreground">
          Pricing
        </Link>
        <Link to="/faq" className="underline underline-offset-2 hover:text-foreground">
          FAQ
        </Link>
      </nav>
```
(Placed between the existing CTA `<div>` and the existing `<nav aria-label="Policies">` block
— adjust the existing block's `mt-10` down to something smaller, e.g. `mt-6`, so the two nav
rows read as a single group rather than two disconnected strips; use your judgment on the
exact spacing token, but keep both `<nav>` elements distinct with their own `aria-label`s
rather than merging them into one list — "Explore" and "Policies" are semantically different
groups and screen-reader users benefit from that distinction.)

### 8.7 — Router wiring

**File:** `frontend/src/app/router.tsx`

Add the five new imports to the existing `@/features/public` import line (currently:
`import { StudioPortfolioPage, ArtistPortfolioPage, SharedDesignPage, EmbedPage,
DiscoverPage, HomePage, PrivacyPolicyPage, TermsOfServicePage, RefundPolicyPage, ContactPage,
UnsubscribePage } from "@/features/public";`) — extend it to also import `FeaturesPage,
PricingPage, UseCaseBookingPage, UseCaseDepositsPage, UseCaseConsentFormsPage, FaqPage`.

Add five new routes in the same top-level block the existing `/privacy`/`/terms`/
`/refund-policy`/`/contact` routes live in (outside `AppRoot`, per that block's own comment —
"Top-level, outside the authenticated AppRoot tree"):
```tsx
{ path: "/features",             element: <FeaturesPage /> },
{ path: "/pricing",              element: <PricingPage /> },
{ path: "/use/booking",          element: <UseCaseBookingPage /> },
{ path: "/use/deposits",         element: <UseCaseDepositsPage /> },
{ path: "/use/consent-forms",    element: <UseCaseConsentFormsPage /> },
{ path: "/faq",                  element: <FaqPage /> },
```

### 8.8 — `features/public/index.ts` exports

Add exports for all six new components (`FeaturesPage`, `PricingPage`,
`UseCaseBookingPage`, `UseCaseDepositsPage`, `UseCaseConsentFormsPage`, `FaqPage`) and the new
`useGetPublicPlansQuery`/`PublicPlanResponse` from `publicApi`, matching the existing
export-list style in that file exactly (one `export { X } from "./components/X"` line per
component, alphabetically grouped the way the existing file already is — check its current
ordering before appending, don't just tack new lines onto the end if the file has an implicit
grouping convention).

---

## 9. Phase 4 — nothing further needed for nav

`PublicPageHeader` (used by every `PublicContentLayout` page, including all six new ones)
already has a "Discover" link plus auth-aware Sign in/Sign up/Register-studio or account
dropdown — it does **not** need Features/Pricing/FAQ links added to it. Adding marketing nav
items to the header that also appears on `/discover`, `/s/:slug`, and `/artist/:slug` (all of
which are *product* surfaces, not marketing surfaces) would blur the line between "browsing
the app" and "reading marketing copy" for users already past the funnel. Keep the new
Features/Pricing/FAQ cross-links scoped to the marketing pages themselves (Home's new nav row,
each page's own closing CTA row) — **do not** modify `PublicPageHeader.tsx` in this prompt.

---

## 10. Phase 5 — Redirect verification

Covered by §6.1's own verification step. No code-side work in this phase — flagged as its own
phase only so the final self-check (§15) has a distinct line item for it.

---

## 11. Help-sync obligations (CLAUDE.md rule 7 — part of done, not an appendix)

**Judgment call, stated explicitly rather than silently skipped:** the six new pages and the
Home update are all **logged-out, unauthenticated marketing surfaces** — nothing an already
signed-in client/artist/owner/admin interacts with as part of using the product, and nothing
that changes what any in-app workflow does. `helpContent.ts` documents in-app features for
users who are already using the product; the standalone user manual
(`frontend/public/user-manual/index.html`) is the same. Neither currently documents `/discover`,
`/s/:slug`, `/artist/:slug`, or any of the four existing policy pages either — this prompt's
new pages are the same category of surface as those, not a new category.

**Verdict: no `helpContent.ts` update, no user-manual update, no onboarding-tour update.**
This is the "zero user-visible surface for an already-authenticated user" carve-out CLAUDE.md
rule 7 itself allows, stated explicitly per that rule's own requirement to say so rather than
leave the question unaddressed — confirm this reasoning still holds by actually checking
`helpContent.ts` and the manual for any existing references to `/discover` or the four policy
pages before finalizing this verdict (if either surface unexpectedly *does* reference public
marketing-adjacent pages already, that would be evidence this verdict is wrong and the new
pages should be added alongside them for consistency — check first, don't assume).

One narrow exception: if `helpContent.ts` or the manual has an existing "how do I share my
studio's page" / "how does someone find my studio" style article that currently only mentions
`/discover` or the direct `/s/:slug` link, consider (and state your decision either way) whether
it's worth a one-line addition pointing owners at the new `/pricing` page as something they can
link customers to — this is optional polish, not a hard requirement, and skipping it with a
one-line "considered, not done, low value" note is an acceptable outcome.

---

## 12. Industry-standard benchmark note (CLAUDE.md rule 6)

Verified 2026-09-26 against the vertical booking/scheduling SaaS category (Fresha, Boulevard,
GlossGenius, Mangomint) via web search — every one of these maintains a public marketing site
structured around Home / Features / Pricing / FAQ as the standard shape, which is exactly what
the to-do doc's own item 3 already specified (this prompt didn't invent the structure — it's
confirmed against the current category norm, not just this repo's own prior plan). Sources:
[Fresha vs GlossGenius](https://glossgenius.com/blog/glossgenius-vs-fresha),
[Mangomint pricing/reviews](https://www.softwareworld.co/software/mangomint-reviews/),
[Boulevard alternatives comparison](https://www.goodcall.com/appointment-scheduling-software/best-boulevard-alternatives-2025).
Separately, current SEO guidance for single-page apps (searched the same date) confirms
prerendering/SSR is the standard fix for the raw-HTML metadata gap this prompt explicitly
defers to section 3 — see [Single Page Application SEO guide](https://www.weweb.io/blog/seo-single-page-application-ultimate-guide)
and [Prerendering for SEO](https://prerender.info/use-cases/saas) — which validates that
deferring it to its own dedicated prompt (rather than half-solving it here with a different
mechanism) is the technically correct sequencing, not a shortcut.

**Deliberate divergence from the benchmark set, flagged per this project's own standing
rule:** none identified for this specific pass — the page set, structure, and zero-commission
pricing framing all match category norms. If a future pass adds testimonials/customer logos
(a near-universal pattern on the benchmark sites' homepages), that's a real gap worth flagging
now for the Marketing project's backlog (to-do section 5) rather than fabricating placeholder
testimonials here, which would be worse than not having the section at all.

---

## 13. Test requirements

**Backend (unit):**
- `GetPublicPlansHandlerTests.cs` per §7.5 — sort order, active-only filtering, DTO field
  exclusion, empty-catalog handling.

**Backend (integration):** only if `GetPlansHandlerTests` has an existing integration
counterpart — mirror it 1:1 for the new endpoint; otherwise unit coverage alone is consistent
with existing convention for this area.

**Frontend (component, one `__tests__` file per new page, mirroring the existing
`PublicContentPages.test.tsx`/`ContactPage.test.tsx` pattern already in
`frontend/src/features/public/__tests__/` — read one of those first for the exact
render-with-router-and-store harness this repo uses before writing new ones):**
- `FeaturesPage.test.tsx` — renders all six feature-group headings; renders both CTA links
  with correct `to` targets; `useDocumentMeta` sets the expected title (assert via
  `document.title` after render, matching whatever assertion style
  `StudioPortfolioPage.test.tsx` already uses for the same hook).
- `PricingPage.test.tsx` — **loading state** (skeleton renders, `aria-busy="true"`); **error
  state** (mocked `isError: true` → contact-us message renders); **empty state** (mocked
  `data: []` → "being finalized" message renders); **success state** (mocked plan list →
  each plan's name/price/interval/highlights render, currency formatting is correct for at
  least two different currency codes if the catalog ever has more than one — otherwise one
  is sufficient); confirm the query is called with no arguments (it's parameterless).
- `UseCaseBookingPage.test.tsx` / `UseCaseDepositsPage.test.tsx` /
  `UseCaseConsentFormsPage.test.tsx` — each renders its H1 and CTA row; keep these lean
  (these are close to static content pages, don't over-test copy).
- `FaqPage.test.tsx` — renders all seeded FAQ questions; accordion items are collapsed by
  default and expand on click/keyboard activation (confirm whatever the wrapped `Accordion`
  component's existing test convention checks, e.g. via `DiscoverPage`/other accordion-using
  page tests if any exist, otherwise write from Radix's documented a11y behavior:
  `aria-expanded` toggles).
- `HomePage.test.tsx` (existing file — extend, don't replace) — add assertions for the three
  new "Explore" links (`/features`, `/pricing`, `/faq`) alongside whatever existing assertions
  cover the current CTAs/policy nav.
- `router.tsx` — if there's an existing route-table smoke test (check
  `frontend/src/app/__tests__/` or similar before assuming there isn't one), extend it with
  the six new paths; if no such test exists, don't invent one just for this prompt — the
  per-page component tests above already prove each route's component renders correctly, and
  a full router-mount smoke test is a separate, larger addition out of this prompt's scope.

**All new/changed test files run clean, plus the full existing suite (no regressions from the
`HomePage.tsx` spacing/nav change or the `router.tsx`/`publicApi.ts`/`index.ts` edits).**

---

## 14. Explicitly deferred (do not build here — separate, already-anticipated prompt)

Per §2.7, this prompt does **not**:
- Fix `frontend/public/robots.txt`'s broken `sitemap.xml` reference or generate a sitemap.
- Add server-rendered/prerendered per-page `<title>`/meta/OG tags for **any** public route
  (existing four, this prompt's new six, or the two dynamic portfolio routes) — the raw-HTML
  gap stays exactly as it is today for all of them.
- Add the missing `frontend/public/og-image.png`.
- Add `noindex` handling for logged-in app pages (that's an existing, separate concern, not
  introduced or worsened by this prompt — the new marketing pages are the *opposite* of what
  needs `noindex`, they're the pages that specifically want to be indexed).
- Touch Google Search Console, Google Analytics, Meta/TikTok/X/LinkedIn domain verification,
  or any of to-do section 4's items — those need real DNS TXT records / third-party console
  access (BLOCKING-MANUAL, Phi-only) and a decision on whether Google Business Profile fits a
  software company with no storefront, none of which this prompt is positioned to resolve.

All of the above stays exactly as described in the to-do doc's sections 3 and 4, to be
consumed by a future, separately-written overnight prompt — this prompt's final summary should
explicitly restate that the raw-HTML SEO problem is unsolved after this ships, so nobody reads
"marketing site shipped" as "SEO is fixed."

**Nothing in this prompt requires a "do not build blind" backlog entry** — every decision
above was either already made in the source consultation (§2) or resolved by reading live
source (§7.1, §7.3's fallback). No pricing/tax/legal/compliance/new-integration decision is
being made here.

---

## 15. Final self-check — run before declaring done

- [ ] `dotnet build "Pena e Arte.slnx" --configuration Release` — clean.
- [ ] `dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj --filter
      "FullyQualifiedName~GetPublicPlans"` — new tests pass.
- [ ] Full backend suite (`dotnet test` across both `UnitTests`/`IntegrationTests` projects) —
      no regressions.
- [ ] `cd frontend && pnpm lint` — clean.
- [ ] `cd frontend && pnpm tsc -b` (or `pnpm build`, which runs this) — no TypeScript errors,
      no `any`.
- [ ] `cd frontend && pnpm test` — full suite green, including every new/updated file in §13.
- [ ] `cd frontend && pnpm test:e2e` — if any existing e2e spec navigates through `/` or the
      public-page set, confirm it still passes; a new e2e spec is not required for this
      prompt unless one of the existing specs breaks and needs updating to account for
      `HomePage.tsx`'s new nav row.
- [ ] `k8s/base/ingress.yaml` — confirm the new `tattooos.co` host and its own
      `pena-e-arte-marketing-tls` secretName are present, `app.tattooos.co`'s existing block
      is byte-for-byte unchanged apart from the new sibling entries, no `www.tattooos.co` host
      was added (per §2.2/§6.2).
- [ ] `docs/claude/architecture.md`'s "AllowAnonymous Exceptions" table has the new
      `/api/v1/billing/plans/public` row with its mitigation, matching the table's existing
      column format exactly.
- [ ] No `console.log`/`Console.WriteLine` introduced (CI's `guardrails` job will also catch
      this, but confirm locally first).
- [ ] No new npm/pnpm/NuGet package was added — `git diff frontend/package.json
      frontend/pnpm-lock.yaml Pena_e_Arte.*/*.csproj` shows no dependency changes.
- [ ] §6.1's BLOCKING-MANUAL Cloudflare steps are either confirmed done (state how — `dig`
      output, redirect curl output) or explicitly flagged as still pending in the final
      summary — never silently assumed.
- [ ] Final summary explicitly states that the raw-HTML SEO gap (§14) is unsolved by this
      change, so a reader doesn't conflate "marketing site shipped" with "SEO fixed."
- [ ] Final summary states the to-do doc's "Cloudflare redirect from tattooos.co and www to
      app.tattooos.co" stopgap item is superseded (§2.3), not implemented, and why.
- [ ] Drift check: `git diff --stat` includes only files named in §6–§9 above plus their test
      files and the two docs updates in §16 — nothing outside that set.

---

## 16. Final deliverable spec

**Code files (new):**
- `Pena_e_Arte.Application/Billing/Queries/GetPublicPlansQuery.cs`
- `tests/Pena_e_Arte.UnitTests/Billing/GetPublicPlansHandlerTests.cs`
- `frontend/src/features/public/components/FeaturesPage.tsx`
- `frontend/src/features/public/components/PricingPage.tsx`
- `frontend/src/features/public/components/UseCaseBookingPage.tsx`
- `frontend/src/features/public/components/UseCaseDepositsPage.tsx`
- `frontend/src/features/public/components/UseCaseConsentFormsPage.tsx`
- `frontend/src/features/public/components/FaqPage.tsx`
- `frontend/src/features/public/__tests__/FeaturesPage.test.tsx`
- `frontend/src/features/public/__tests__/PricingPage.test.tsx`
- `frontend/src/features/public/__tests__/UseCaseBookingPage.test.tsx`
- `frontend/src/features/public/__tests__/UseCaseDepositsPage.test.tsx`
- `frontend/src/features/public/__tests__/UseCaseConsentFormsPage.test.tsx`
- `frontend/src/features/public/__tests__/FaqPage.test.tsx`

**Code files (edited):**
- `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs` (+1 route registration)
- `frontend/src/features/public/publicApi.ts` (+1 query, +1 type)
- `frontend/src/features/public/components/HomePage.tsx` (+ Explore nav row)
- `frontend/src/features/public/index.ts` (+ new exports)
- `frontend/src/app/router.tsx` (+ import line extended, +6 routes)
- `frontend/src/features/public/__tests__/HomePage.test.tsx` (+ new nav assertions)
- `k8s/base/ingress.yaml` (+1 tls entry, +1 rule)

**Docs (edited, part of this prompt's own deliverable, not left to a later pass):**
- `docs/claude/architecture.md` — two additions:
  1. New row in "AllowAnonymous Exceptions" (§7.4).
  2. New Decisions Log entry, mirroring the existing entries' style (see the "Public Portfolio
     Pages — Nav Header — 2026-07-04" entry this prompt's own §1 investigation found as a
     format reference), titled `## Marketing Site at the Apex Domain — 2026-09-26`, covering:
     the problem (no site at `tattooos.co`), the decision (extend the existing SPA rather than
     a separate static build or third-party site builder — summarizing §5), what shipped
     (page list, new public endpoint, Ingress change), and explicitly noting the deferred
     raw-HTML SEO work per §14 as a pointer for whoever picks that up next.
- `docs/claude/infra-log-email-domain-setup-2026-09-26.md` — **not touched by this prompt**
  (unrelated; if it's still uncommitted per this prompt's own header note, commit it as its
  own standalone first commit before branching, don't fold it into this feature).

**Claude Docs update (outside this repo — cannot be done by the implementing session, flag it
back to the requester instead):** once this ships, section 2's checklist item 3 ("Build the
marketing site on `tattooos.co`...") and item 1 ("Decide where the marketing site lives...")
in the `TattooOS domain, website and search to-do` Claude Docs page should be checked off, and
item 2 ("Stopgap: Cloudflare redirect...") should be checked off as superseded — the
implementing Claude Code session has no access to that Claude Docs page (it lives outside this
repo, in the separate consultation project), so state this explicitly in the final summary as
a manual follow-up for Phi/the consultation project, do not attempt to reach it.

**Commit message:**
```
git add -A && git commit -m "feat: marketing site at tattooos.co — features, pricing, use-case pages, FAQ"
```

**PR description should note:** this ships the marketing site but explicitly does not fix
per-page raw-HTML SEO metadata (deferred, §14) — reviewers should not expect a Lighthouse SEO
score jump from this change, only from the follow-up prompt that picks up to-do section 3.
