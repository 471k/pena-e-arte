# Overnight Prompt — In-App Help Menu (Guides + Search + FAQ, All 4 Roles)
**Date:** 2026-07-20
**Output:** new `frontend/src/features/help/` module, wired into all four role layouts
**Type:** New frontend-only feature. No backend changes. No new entity, no new endpoint.

---

## Task in One Sentence

Build a searchable, role-aware in-app Help menu — a header button that opens a panel with
step-by-step guides for every screen the current role can see, plus an FAQ tab — mirroring
the `FeedbackDialog` integration pattern already used in every layout, sourced from a static
content module (no backend), and grounded in the actual codebase, not invented copy.

---

## Why This Is Frontend-Only (read before writing any backend code)

This feature has **zero backend footprint**, same as Feature #7 (Studio Map: "No entity...
None — public endpoint") in `docs/claude/architecture.md`'s Feature Module Map. Help content
is static product documentation, not tenant data or user data — it does not need EF Core, an
endpoint, a MediatR command, or a FluentValidation validator. Do not create any of those.
If you find yourself adding a controller, an entity, or a migration for this feature, stop —
that means the design has drifted from the spec.

The only "backend" involved is reading `docs/claude/architecture.md`, `frontend.md`, and the
actual page components, to write accurate plain-English content.

---

## Phase 1 — Required Reading (do not skip)

This spec deliberately reuses the file list and role/route map already validated in
`docs/claude/overnight-prompt-user-manual-2026-07-04.md` (the standalone offline manual at
`frontend/public/user-manual/index.html`). That file's Phase 1 reading list and Phase 4
section table are the source of truth for which screens exist and who can access them —
read that file first, then re-verify against current code before writing content, because
routes may have changed since 2026-07-04 (check `frontend/src/app/router.tsx` directly,
it is dated 2026-07-20 in this read and is the ground truth for current routes/roles).

Read, in order:

1. `frontend/src/app/router.tsx` — current routes and `RoleGuard allowedRoles` per route.
   This supersedes the route table in the 2026-07-04 manual prompt if they disagree.
2. `frontend/src/layouts/ClientLayout.tsx`, `ArtistLayout.tsx`, `OwnerLayout.tsx`,
   `IssuerLayout.tsx` — exact header structure, to know where the Help button goes.
3. `frontend/src/features/feedback/` (all 6 files) — this is the pattern to mirror.
   `FeedbackDialog` is opened from a `useState` boolean in each layout, rendered as a
   sibling of `NotificationBell` in the header's `ml-auto` icon cluster. Help does the same.
4. `frontend/public/user-manual/index.html` — if it exists, read it for section content,
   step order, and wording. Do not copy prose verbatim (it's written for a standalone page,
   ours is shorter and interactive) but reuse its verified role/route/feature mapping so the
   two documents don't drift out of sync.
5. Every page component listed in Phase 4 below — confirm each still exists at that path,
   confirm the actual RBAC gate in its route (cross-check against `router.tsx`), and confirm
   the actual step order of its primary form/flow (date picker before artist picker, etc.).
6. `docs/claude/architecture.md` sections: "Payment Architecture — Card & Cash Only",
   "Platform Subscription Architecture" (trial/grace-period rules), "Multi-Studio Client
   View" (#23), "Client Portable Profiles" — these back several FAQ answers below and must
   be described accurately (e.g. do not say "PayPal" anywhere — it is not used).

---

## Phase 2 — Module Structure

Create `frontend/src/features/help/`, following the exact shape of `features/feedback/`:

```
frontend/src/features/help/
├── help.types.ts              HelpRole, HelpArticle, FaqItem, HelpSearchResult types
├── helpContent.ts             the static content: HELP_ARTICLES[], FAQ_ITEMS[]
├── helpSearch.ts              pure search/scoring function, no React
├── components/
│   ├── HelpMenu.tsx            trigger button + Sheet, owns open/closed state internally
│   ├── HelpSearchInput.tsx     search box + live result list
│   ├── HelpArticleView.tsx     renders one HelpArticle (steps, tips, warnings, "Go to page")
│   └── FaqAccordion.tsx        renders FaqItem[] using shadcn Accordion
├── index.ts                   export HelpMenu only (public surface — mirrors feedback/index.ts)
└── __tests__/
    ├── helpContent.test.ts     data-integrity test (no rendering)
    ├── helpSearch.test.ts      scoring/filtering unit tests
    ├── HelpMenu.test.tsx
    ├── HelpSearchInput.test.tsx
    └── FaqAccordion.test.tsx
```

No `helpApi.ts`, no Redux slice, no entry in `app/store.ts`. Content is a plain imported
array — same reasoning as the Studio Map feature ("No Redux slice needed").

---

## Phase 3 — Types (`help.types.ts`)

```typescript
export const HelpRole = {
  Client: "client",
  Artist: "artist",
  Owner:  "owner",
  Issuer: "issuer",
} as const;
export type HelpRole = typeof HelpRole[keyof typeof HelpRole];
// Reuses the same string values as shared/types/roles.ts Role — intentionally
// compatible so `role as unknown as HelpRole` is never needed; import Role directly
// where possible and only introduce HelpRole if the sets diverge (they should not).

export interface HelpArticle {
  id: string;                    // stable slug, e.g. "client-book-appointment"
  roles: HelpRole[];              // which role(s) see this article verbatim
  title: string;                  // plain English, no jargon
  route?: string;                 // e.g. "/book" — renders a "Go to this page" button if set
  keywords: string[];             // extra search terms: synonyms, jargon a user might type
  summary: string;                // 1–2 plain sentences, shown in search results
  steps: string[];                // ordered, matches actual on-screen flow
  tips?: string[];
  warnings?: string[];            // destructive/irreversible actions
  relatedArticleIds?: string[];   // must reference real ids — enforced by helpContent.test.ts
}

export interface FaqItem {
  id: string;
  roles: HelpRole[];               // FAQ shown to these roles; use all four for universal FAQs
  question: string;
  answer: string;                  // plain text or short markdown-free paragraph
  relatedArticleIds?: string[];
}

export interface HelpSearchResult {
  type: "article" | "faq";
  id: string;
  score: number;                   // higher = better match, see helpSearch.ts
  matchedOn: "title" | "keyword" | "body" | "question";
}
```

---

## Phase 4 — Content to Write

Write one `HelpArticle` per row below. Use the same section granularity as the 2026-07-04
manual prompt's Phase 4 tables (same section IDs, reused as `HelpArticle.id` with role
prefix already baked in — no translation needed, copy the ID column directly). For each,
read the actual component file first, then write `summary` + `steps` + `tips`/`warnings`
from what the code does — do not invent UI that isn't there.

**Writing rules (same as the 2026-07-04 manual, apply again here):**
- Plain English only. Never say "API", "RTK Query", "tenant", "endpoint", "mutation" in
  content shown to users. Say "studio", "your account", "the app".
- Step order must match the actual on-screen order in the component.
- Warnings for anything irreversible (leaving a studio, deleting a deposit rule, revoking
  a share link, cancelling a subscription).
- Do not mention PayPal anywhere — it is not used (card via Stripe, or cash, only).
- Where a screen behaves differently per role (e.g. `/designs` for client vs artist vs
  owner), write a **separate article per role** rather than one article with branching
  text — this keeps search results role-relevant and keeps `HelpArticleView` simple.

### 4.1 Client articles (`roles: [Client]`)

| id | title | route |
|---|---|---|
| `client-book-appointment` | Book an appointment | `/book` |
| `client-my-studios` | Switch between studios you're a client at | `/my-studios` |
| `client-designs` | View your tattoo designs | `/designs` |
| `client-design-approve` | Approve a design or request changes | `/designs/:id` |
| `client-intake-submit` | Fill out an intake form | `/forms/intake/new` |
| `client-intake-list` | Find your submitted intake forms | `/forms/intake` |
| `client-consent-sign` | Sign a consent form | `/forms/consent/new` |
| `client-consent-list` | Find your signed consent forms | `/forms/consent` |
| `client-profile` | Update your profile and body map | `/clients/me` |
| `client-deposit-pay` | Pay a deposit (card or cash) | `/pay/:paymentId` |
| `client-verify-email` | Verify your email address | `/verify-email` |
| `client-change-password` | Change your password | `/account/change-password` |

### 4.2 Artist articles (`roles: [Artist]`)

| id | title | route |
|---|---|---|
| `artist-schedule` | View and manage your schedule | `/schedule` |
| `artist-appointment-detail` | Open an appointment's details | `/appointments/:id` |
| `artist-clients` | Find a client you're working with | `/clients` |
| `artist-client-detail` | View a client's profile and tattoo history | `/clients/:id` |
| `artist-designs` | View designs assigned to you | `/designs` |
| `artist-create-design` | Create a new design for a client | `/designs/new` |
| `artist-upload-revision` | Upload a revised design | `/designs/:id/upload` |
| `artist-share-design` | Share a design link with a client | (button on design detail) |
| `artist-intake-view` | Review a client's intake form | `/forms/intake` |
| `artist-consent-view` | Review a client's signed consent form | `/forms/consent` |
| `artist-notifications` | Check your notifications | (bell icon in header) |

### 4.3 Owner articles (`roles: [Owner]`)

| id | title | route |
|---|---|---|
| `owner-dashboard` | Understand your dashboard | `/dashboard` |
| `owner-artists-add` | Add an artist to your studio | `/artists/new` |
| `owner-artists-list` | Manage your artists | `/artists` |
| `owner-clients-add` | Add a client manually | `/clients/new` |
| `owner-clients-list` | Manage your clients | `/clients` |
| `owner-designs` | Review and manage all studio designs | `/designs` |
| `owner-deposit-rules` | Set up deposit rules | `/deposit-rules` |
| `owner-deposit-rule-create` | Create a new deposit rule | `/deposit-rules/new` |
| `owner-schedule` | View the studio-wide schedule | `/schedule` |
| `owner-payments` | Track deposits and payments | `/payments` |
| `owner-payment-create` | Create a payment request manually | `/payments/new` |
| `owner-cash-confirm` | Confirm a cash deposit was received | (button on payment detail) |
| `owner-studio-profile` | Edit your studio profile | `/studios/me` |
| `owner-branding` | Turn off "Powered by Pena e Artë" branding | (card on studio profile) |
| `owner-embed` | Get your booking widget embed code | (card on studio profile) |
| `owner-qr-code` | Download your studio's QR code | (card on studio profile) |
| `owner-referral` | Get your referral code | (card on studio profile) |
| `owner-notifications` | Review sent notifications | `/notifications` |
| `owner-billing` | Understand your plan, trial, and billing | `/billing` |
| `owner-subscribe` | Choose or change your subscription plan | `/billing/subscribe` |
| `owner-instagram-sync` | Connect Instagram to sync your artists' posts | (Instagram tab, artist profile) |

### 4.4 Issuer articles (`roles: [Issuer]`)

| id | title | route |
|---|---|---|
| `issuer-dashboard` | Read the platform dashboard | `/platform` |
| `issuer-studios` | Oversee all studios | `/platform/studios` |
| `issuer-studio-detail` | Open a single studio's detail | `/platform/studios/:studioId` |
| `issuer-suspend-studio` | Suspend or unsuspend a studio | (button on studio detail) |
| `issuer-plans` | Manage subscription plans | `/platform/plans` |
| `issuer-plan-edit` | Create or edit a plan | `/platform/plans/new` |
| `issuer-subscriptions` | Oversee all subscriptions | `/platform/subscriptions` |
| `issuer-extend-trial` | Extend a studio's trial | (button on subscriptions page) |
| `issuer-activate-cash-sub` | Activate a subscription paid by cash | (button on subscriptions page) |
| `issuer-referrals` | Manage platform referral codes | `/platform/referrals` |
| `issuer-reports` | Read monthly industry reports | `/platform/reports` |
| `issuer-feedback` | Read feedback submitted by owners | `/platform/feedback` |

Also add, for Issuer only:

> `issuer-note-full-access` — not a routed article, an inline banner shown at the top of the
> Guides tab when `role === Issuer`: "As a platform admin you can also open every Client,
> Artist, and Owner screen for support purposes. Toggle below to search their guides too."
> This drives the toggle described in Phase 5.

### 4.5 FAQ (`FAQ_ITEMS`, 18 minimum)

Write these from the actual behavior documented in `architecture.md` — do not soften or
simplify away real constraints (e.g. the 14-day trial + 7-day grace period is exact, state
it exactly).

| id | roles | question |
|---|---|---|
| `faq-trial-length` | owner | How long is the free trial, and what happens when it ends? |
| `faq-grace-period` | owner | What is the 7-day grace period, and what can I do during it? |
| `faq-cash-vs-card` | owner, client | What's the difference between paying by card and paying by cash? |
| `faq-cash-confirm-who` | owner, artist | Who has to confirm a cash deposit was received? |
| `faq-deposit-statuses` | owner, artist, client | What do the different deposit statuses mean? |
| `faq-design-statuses` | client, artist, owner | What do the different design statuses mean? |
| `faq-appointment-statuses` | client, artist, owner | What do the different appointment statuses mean? |
| `faq-multi-studio` | client | I go to more than one studio — can I use one account for both? |
| `faq-portable-profile` | client | Can a new studio see my tattoo history from another studio? |
| `faq-branding-removal` | owner | Can I remove the "Powered by Pena e Artë" footer? |
| `faq-referral-reward` | owner | What do I get for referring another studio? |
| `faq-share-design-expiry` | client, artist, owner | How long does a shared design link last? |
| `faq-qr-code-use` | owner | What is the studio QR code for? |
| `faq-instagram-sync-frequency` | owner, artist | How often does Instagram sync run? |
| `faq-notification-channels` | client, artist, owner | How will I be notified about appointments? |
| `faq-forgot-password` | client, artist, owner, issuer | I forgot my password — what do I do? |
| `faq-issuer-suspend-effect` | issuer | What happens to a studio immediately after I suspend it? |
| `faq-issuer-cash-subscription` | issuer | How do I activate a studio's subscription if they paid by cash? |

Each `answer` must be grounded: e.g. `faq-trial-length` states "14 days, full access, no
card required," `faq-grace-period` states "7 days of read-only access after the trial ends,
then the studio is suspended until they subscribe" — pulled directly from architecture.md's
Trial Model section, not paraphrased loosely.

---

## Phase 5 — `helpSearch.ts` (pure function, unit-testable, no React)

```typescript
import type { HelpArticle, FaqItem, HelpSearchResult } from "./help.types";

export function searchHelp(
  query: string,
  articles: HelpArticle[],
  faqs: FaqItem[],
): HelpSearchResult[] {
  const q = query.trim().toLowerCase();
  if (q.length < 2) return [];

  const results: HelpSearchResult[] = [];

  for (const a of articles) {
    const title = a.title.toLowerCase();
    if (title.includes(q)) {
      results.push({ type: "article", id: a.id, score: 100, matchedOn: "title" });
      continue;
    }
    if (a.keywords.some((k) => k.toLowerCase().includes(q))) {
      results.push({ type: "article", id: a.id, score: 60, matchedOn: "keyword" });
      continue;
    }
    const body = [a.summary, ...a.steps].join(" ").toLowerCase();
    if (body.includes(q)) {
      results.push({ type: "article", id: a.id, score: 30, matchedOn: "body" });
    }
  }

  for (const f of faqs) {
    if (f.question.toLowerCase().includes(q)) {
      results.push({ type: "faq", id: f.id, score: 90, matchedOn: "question" });
    } else if (f.answer.toLowerCase().includes(q)) {
      results.push({ type: "faq", id: f.id, score: 25, matchedOn: "body" });
    }
  }

  return results.sort((a, b) => b.score - a.score);
}
```

Pure substring matching, same approach the 2026-07-04 manual prompt used ("pure substring
matching, no library needed") — do not add Fuse.js or any fuzzy-search npm package; the
content set is under 100 items and substring scoring is sufficient. `articles` and `faqs`
passed in are already pre-filtered to the caller's role (filtering happens in `HelpMenu`,
not in `helpSearch`, so this function stays pure and independently testable).

---

## Phase 6 — `HelpMenu.tsx` (the component wired into layouts)

Mirror `FeedbackDialog`'s controlled-open pattern, but `HelpMenu` owns its own `open` state
internally (unlike `FeedbackDialog`, which is controlled from the layout) since no layout
needs to open Help programmatically — this keeps the layout diff to a single import + one
JSX line, smaller than the Feedback integration.

```tsx
// features/help/components/HelpMenu.tsx
import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { HelpCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/shared/components/ui/sheet";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import { useAppSelector } from "@/app/hooks";
import { HELP_ARTICLES, FAQ_ITEMS } from "../helpContent";
import { searchHelp } from "../helpSearch";
import { HelpSearchInput } from "./HelpSearchInput";
import { HelpArticleView } from "./HelpArticleView";
import { FaqAccordion } from "./FaqAccordion";
import type { HelpRole } from "../help.types";

export function HelpMenu() {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [selectedArticleId, setSelectedArticleId] = useState<string | null>(null);
  const [showAllRoles, setShowAllRoles] = useState(false); // issuer-only toggle
  const role = useAppSelector((s) => s.auth.role) as HelpRole | null;
  const navigate = useNavigate();

  const scopedArticles = useMemo(() => {
    if (!role) return [];
    if (role === "issuer" && showAllRoles) return HELP_ARTICLES;
    return HELP_ARTICLES.filter((a) => a.roles.includes(role));
  }, [role, showAllRoles]);

  const scopedFaqs = useMemo(() => {
    if (!role) return [];
    return FAQ_ITEMS.filter((f) => f.roles.includes(role));
  }, [role]);

  const results = useMemo(
    () => searchHelp(query, scopedArticles, scopedFaqs),
    [query, scopedArticles, scopedFaqs],
  );

  const selectedArticle = scopedArticles.find((a) => a.id === selectedArticleId) ?? null;

  function handleGoToPage(route: string) {
    setOpen(false);
    navigate(route);
  }

  function handleClose(next: boolean) {
    setOpen(next);
    if (!next) {
      setQuery("");
      setSelectedArticleId(null);
    }
  }

  return (
    <>
      <Button
        variant="ghost" size="icon" className="h-8 w-8"
        onClick={() => setOpen(true)}
        title="Help" aria-label="Open help menu"
      >
        <HelpCircle className="h-4 w-4" />
      </Button>

      <Sheet open={open} onOpenChange={handleClose}>
        <SheetContent side="right" className="w-full sm:max-w-md flex flex-col">
          <SheetHeader>
            <SheetTitle>Help</SheetTitle>
          </SheetHeader>

          <HelpSearchInput value={query} onChange={setQuery} autoFocus />

          {query.length >= 2 ? (
            <SearchResultsList
              results={results}
              articles={scopedArticles}
              faqs={scopedFaqs}
              onSelectArticle={setSelectedArticleId}
            />
          ) : selectedArticle ? (
            <HelpArticleView
              article={selectedArticle}
              onBack={() => setSelectedArticleId(null)}
              onGoToPage={handleGoToPage}
              onSelectRelated={setSelectedArticleId}
            />
          ) : (
            <Tabs defaultValue="guides" className="flex-1 flex flex-col overflow-hidden">
              <TabsList>
                <TabsTrigger value="guides">Guides</TabsTrigger>
                <TabsTrigger value="faq">FAQ</TabsTrigger>
              </TabsList>
              <TabsContent value="guides" className="flex-1 overflow-y-auto">
                {role === "issuer" && (
                  <IssuerAllRolesToggle checked={showAllRoles} onChange={setShowAllRoles} />
                )}
                <GuideList articles={scopedArticles} onSelect={setSelectedArticleId} />
              </TabsContent>
              <TabsContent value="faq" className="flex-1 overflow-y-auto">
                <FaqAccordion items={scopedFaqs} />
              </TabsContent>
            </Tabs>
          )}
        </SheetContent>
      </Sheet>
    </>
  );
}
```

`SearchResultsList`, `GuideList`, `IssuerAllRolesToggle` can be private helper components in
the same file (not exported) or split out — engineer's judgment, keep `HelpMenu.tsx` under
~200 lines; split if it grows past that.

**Keyboard shortcut:** add a `useEffect` in `HelpMenu` (this is a keyboard listener, not data
fetching, so it does not violate the "no useEffect for data fetching" rule) that opens the
sheet on `Shift+?` when no input/textarea is focused:

```tsx
useEffect(() => {
  function handler(e: KeyboardEvent) {
    const target = e.target as HTMLElement;
    const isTyping = ["INPUT", "TEXTAREA"].includes(target.tagName);
    if (e.key === "?" && e.shiftKey && !isTyping) {
      e.preventDefault();
      setOpen(true);
    }
  }
  window.addEventListener("keydown", handler);
  return () => window.removeEventListener("keydown", handler);
}, []);
```

---

## Phase 7 — Layout Integration (exact diff, all four layouts)

In each of `ClientLayout.tsx`, `ArtistLayout.tsx`, `OwnerLayout.tsx`, `IssuerLayout.tsx`:

1. Add `import { HelpMenu } from "@/features/help";`
2. Insert `<HelpMenu />` inside the `ml-auto flex items-center gap-3` header div, directly
   before `<NotificationBell />` (after the feedback button, if that layout has one — check
   each layout individually, `OwnerLayout` has Feedback + NotificationBell + UserMenu in that
   order per the read above; place Help between Feedback and NotificationBell in every layout
   that has all three, and adapt order sensibly in layouts missing one of them).

This is a 2-line diff per layout file. Do not restructure the header otherwise.

Verify `ClientLayout.tsx`, `ArtistLayout.tsx`, and `IssuerLayout.tsx` each have an equivalent
`ml-auto` icon cluster before editing — read them (not just `OwnerLayout.tsx`) to confirm the
exact insertion point per file, since they may differ slightly (e.g. Client layout may not
have a Feedback button, or may have a different icon order).

---

## Phase 8 — Accessibility & UX Details

- `Sheet` from shadcn already traps focus and closes on `Escape` — no extra work needed.
- `HelpSearchInput` renders a native `<input type="search">` with `aria-label="Search help"`.
- Empty search results state: "No results for '<query>'. Try a different word, or browse
  Guides and FAQ below." with a button to clear the query.
- `FaqAccordion` uses shadcn `Accordion` (`type="single" collapsible`) — one FAQ open at a
  time, standard behavior, no custom animation needed.
- Every warning (`HelpArticle.warnings`) renders in a `<div role="note">` with a distinct
  amber/red treatment consistent with existing `callout-warn` styling conventions used in
  the codebase's own alert components (check `shared/components/ui/alert.tsx` if present
  and reuse it rather than inventing new warning markup).
- Language: English only, consistent with in-app UI. Do not add Albanian translations in
  this pass — note as a follow-up in the exit summary if the app has i18n elsewhere (check
  for an existing i18n setup before assuming there is none).

---

## Phase 9 — Tests

`helpContent.test.ts` (data integrity, no rendering, fast):
```typescript
describe("helpContent", () => {
  it("has no duplicate article ids", () => { /* ... */ });
  it("has no duplicate faq ids", () => { /* ... */ });
  it("every relatedArticleId references a real article", () => { /* ... */ });
  it("every article has at least one role", () => { /* ... */ });
  it("every faq has at least one role", () => { /* ... */ });
  it("every article with a route starts with '/'", () => { /* ... */ });
});
```

`helpSearch.test.ts`:
```typescript
describe("searchHelp", () => {
  it("returns empty array for queries under 2 characters", () => {});
  it("ranks title matches above keyword matches", () => {});
  it("ranks keyword matches above body matches", () => {});
  it("matches are case-insensitive", () => {});
  it("returns faq question matches", () => {});
});
```

`HelpMenu.test.tsx` (RTL, mock `useAppSelector` for role):
```typescript
describe("HelpMenu", () => {
  it("opens the sheet when the help button is clicked", () => {});
  it("opens the sheet on Shift+? when no input is focused", () => {});
  it("does not open on Shift+? while typing in a text field", () => {});
  it("only shows articles matching the current role", () => {});
  it("shows the all-roles toggle only for issuer", () => {});
  it("navigates and closes the sheet when Go to this page is clicked", () => {});
});
```

Follow the `MethodName_Scenario_ExpectedResult`-equivalent `describe`/`it` convention from
`docs/claude/conventions.md`.

---

## Phase 10 — Verification Checklist

1. `pnpm lint` — zero new errors. No `any` introduced anywhere in `features/help/`.
2. `pnpm test` — all new tests pass, no existing layout tests broken by the `<HelpMenu />`
   insertion (check `layouts/__tests__/*.test.tsx` still pass; they may need a mock for the
   new import if they shallow-render layouts).
3. Manually confirm in dev: log in as each of the four roles, open Help via button and via
   `Shift+?`, confirm only role-appropriate articles appear, confirm search narrows results,
   confirm FAQ tab renders, confirm "Go to this page" navigates and closes the sheet.
4. Confirm issuer sees the all-roles toggle and it correctly expands the guide list when on.
5. Confirm no backend files were touched — `git diff --stat` should show only files under
   `frontend/src/features/help/`, the four layout files, and this doc.

---

## Exit Condition

All tests pass, lint is clean, manual walkthrough for all four roles confirms correct
role-scoping. Then append to `docs/claude/architecture.md` Feature Module Map:

```markdown
| 39 | In-App Help Menu | No entity (static content) | None — frontend-only, no backend | All roles |
```

And a short prose block under the Feature Module Map table:

```markdown
### In-App Help Menu — 2026-07-20

Searchable, role-scoped help panel opened from every layout header (mirrors the
FeedbackDialog integration pattern). Content lives entirely in
`frontend/src/features/help/helpContent.ts` — no backend, no entity, no endpoint.
Search is plain substring scoring in `helpSearch.ts` (title > keyword > body), same
approach as the standalone manual at `frontend/public/user-manual/index.html`. Issuer
role gets an additional toggle to browse Client/Artist/Owner guides for support purposes.
Keep this file and the standalone manual in sync when either is updated — they cover the
same screens from two different delivery mechanisms (in-app panel vs. offline document).
```
