# Overnight Prompt — Pena e Artë User Manual (Self-Contained Web App)
**Date:** 2026-07-04
**Output:** `frontend/public/user-manual/index.html`
**Type:** Deep-dive research → generate. No application code is changed.

---

## Task in One Sentence

Read every frontend page component in the codebase, understand exactly what each screen does
and which roles access it, then write a single self-contained HTML user manual with inline
SVG wireframes, step-by-step workflows, and a full offline-capable navigation system.

---

## Phase 1 — Required Reading (do not skip any file)

Read every file in the list below **before writing a single line of HTML**. Build a mental
model of what each screen renders, what its forms do, what API calls it makes, and what
role can access it. Reading the test files alongside the page components is recommended —
tests describe exact behaviour that must appear in the manual.

### Routes and Layouts (read first — they define the role map)

```
frontend/src/app/router.tsx
frontend/src/layouts/ClientLayout.tsx
frontend/src/layouts/ArtistLayout.tsx
frontend/src/layouts/OwnerLayout.tsx
frontend/src/layouts/IssuerLayout.tsx
frontend/src/shared/types/roles.ts
frontend/src/shared/hooks/usePermission.ts
```

### Auth & Registration

```
frontend/src/features/auth/components/LoginPage.tsx
frontend/src/features/auth/components/ClientRegisterPage.tsx
frontend/src/features/studios/components/RegisterStudioPage.tsx
frontend/src/features/auth/components/ForgotPasswordPage.tsx
frontend/src/features/auth/components/ResetPasswordPage.tsx
frontend/src/features/auth/components/VerifyEmailPage.tsx
frontend/src/features/auth/components/ChangePasswordPage.tsx
frontend/src/features/auth/components/MyStudiosPage.tsx
frontend/src/features/auth/authApi.ts
frontend/src/features/auth/authSlice.ts
```

### Public / Guest Pages

```
frontend/src/features/public/components/DiscoverPage.tsx
frontend/src/features/public/components/StudioPortfolioPage.tsx
frontend/src/features/public/components/ArtistPortfolioPage.tsx
frontend/src/features/public/components/PortfolioFeed.tsx
frontend/src/features/public/components/ReviewSection.tsx
frontend/src/features/public/components/EmbedPage.tsx
frontend/src/features/public/components/SharedDesignPage.tsx
frontend/src/features/map/components/StudioMapPage.tsx
frontend/src/features/public/publicApi.ts
```

### Client-Facing Pages

```
frontend/src/features/appointments/components/BookPage.tsx
frontend/src/features/appointments/components/AppointmentDetailPage.tsx
frontend/src/features/designs/components/DesignListPage.tsx
frontend/src/features/designs/components/DesignDetailPage.tsx
frontend/src/features/forms/components/SubmitIntakeFormPage.tsx
frontend/src/features/forms/components/IntakeFormListPage.tsx
frontend/src/features/forms/components/IntakeFormDetailPage.tsx
frontend/src/features/forms/components/SignConsentFormPage.tsx
frontend/src/features/forms/components/ConsentFormListPage.tsx
frontend/src/features/forms/components/ConsentFormDetailPage.tsx
frontend/src/features/clients/components/MyProfilePage.tsx
frontend/src/features/clients/components/BodyMap.tsx
frontend/src/features/payments/components/DepositCheckoutPage.tsx
```

### Artist-Facing Pages

```
frontend/src/features/appointments/components/SchedulePage.tsx
frontend/src/features/appointments/components/AppointmentCard.tsx
frontend/src/features/designs/components/CreateDesignPage.tsx
frontend/src/features/designs/components/UploadRevisionPage.tsx
frontend/src/features/designs/components/ShareDesignButton.tsx
frontend/src/features/artists/components/ArtistDetailPage.tsx
```

### Owner-Facing Pages

```
frontend/src/features/dashboard/components/DashboardPage.tsx
frontend/src/features/dashboard/components/SetupChecklist.tsx
frontend/src/features/artists/components/ArtistListPage.tsx
frontend/src/features/artists/components/ArtistCard.tsx
frontend/src/features/artists/components/CreateArtistPage.tsx
frontend/src/features/clients/components/ClientListPage.tsx
frontend/src/features/clients/components/ClientCard.tsx
frontend/src/features/clients/components/CreateClientPage.tsx
frontend/src/features/clients/components/ClientDetailPage.tsx
frontend/src/features/clients/components/TattooHistorySection.tsx
frontend/src/features/clients/components/TattooRecordDetailPage.tsx
frontend/src/features/designs/components/DesignCard.tsx
frontend/src/features/designs/components/DesignStatusBadge.tsx
frontend/src/features/deposit-rules/components/DepositRuleListPage.tsx
frontend/src/features/deposit-rules/components/DepositRuleCard.tsx
frontend/src/features/deposit-rules/components/CreateDepositRulePage.tsx
frontend/src/features/deposit-rules/components/DepositRuleDetailPage.tsx
frontend/src/features/payments/components/PaymentListPage.tsx
frontend/src/features/payments/components/PaymentDetailPage.tsx
frontend/src/features/payments/components/CreatePaymentIntentPage.tsx
frontend/src/features/payments/components/PaymentMethodSelector.tsx
frontend/src/features/studios/components/StudioProfilePage.tsx
frontend/src/features/studios/components/BrandingSettingsCard.tsx
frontend/src/features/studios/components/EmbedCodeCard.tsx
frontend/src/features/studios/components/ReferralCodeCard.tsx
frontend/src/features/notifications/components/NotificationLogListPage.tsx
frontend/src/features/notifications/components/NotificationPreferencesCard.tsx
frontend/src/features/notifications/components/NotificationBell.tsx
frontend/src/features/billing/components/BillingPage.tsx
frontend/src/features/billing/components/SubscribePage.tsx
frontend/src/features/appointments/components/AppointmentStatusBadge.tsx
frontend/src/features/appointments/components/DepositStatusBadge.tsx
```

### Issuer-Facing Pages

```
frontend/src/features/platform/components/IssuerDashboardPage.tsx
frontend/src/features/platform/components/IssuerStudioListPage.tsx
frontend/src/features/platform/components/IssuerStudioDetailPage.tsx
frontend/src/features/platform/components/PlanManagementPage.tsx
frontend/src/features/platform/components/SubscriptionOversightPage.tsx
frontend/src/features/platform/components/PlatformReferralPage.tsx
frontend/src/features/platform/components/IndustryReportsPage.tsx
frontend/src/features/platform/components/MrrChart.tsx
frontend/src/features/feedback/components/FeedbackInboxPage.tsx
```

### Domain types (for status glossary)

```
frontend/src/features/designs/design.types.ts
frontend/src/features/appointments/appointment.types.ts    ← if it exists
frontend/src/features/notifications/notification.types.ts
frontend/src/features/payments/payment.types.ts            ← if it exists
```

---

## Phase 2 — Output Specification

**File:** `frontend/public/user-manual/index.html`
(Accessible in development at `http://localhost:5173/user-manual/index.html`)

**Constraints:**
- 100% self-contained. Zero external CDN links. All CSS and JS inline in the file.
- No npm packages, no framework. Vanilla HTML + CSS + JS only.
- SVG wireframes for every major screen, drawn from code understanding — not real screenshots.
- Must work offline: open `index.html` from disk with no server running.
- Print-friendly: `@media print` CSS so individual sections can be printed.
- Max single file size: aim for under 600 KB of HTML (aggressive but achievable with
  compressed SVG and no bloat).

---

## Phase 3 — Architecture of the HTML File

### Document structure

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Pena e Artë — User Manual</title>
  <style>/* ALL CSS HERE — no <link> tags */</style>
</head>
<body>
  <!-- Skip link for accessibility -->
  <a href="#main-content" class="skip-link">Skip to main content</a>

  <!-- Top bar: logo + search + print button -->
  <header id="topbar">...</header>

  <div id="app-shell">
    <!-- Left sidebar: role tabs + section tree -->
    <nav id="sidebar" aria-label="Manual navigation">...</nav>

    <!-- Main reading area -->
    <main id="main-content">...</main>
  </div>

  <script>/* ALL JS HERE — no <script src> tags */</script>
</body>
</html>
```

### CSS system (write all of this inline in `<style>`)

Use CSS custom properties — no Tailwind, no Bootstrap:

```css
:root {
  --bg:          #0a0a0f;
  --bg-card:     #111118;
  --bg-sidebar:  #0d0d14;
  --border:      rgba(255,255,255,.08);
  --text:        #f4f4f5;
  --text-muted:  rgba(244,244,245,.55);
  --accent:      #7c3aed;      /* violet-600 — matches the app */
  --green:       #22c55e;
  --amber:       #f59e0b;
  --red:         #ef4444;
  --blue:        #3b82f6;
  --radius:      8px;
  --sidebar-w:   260px;
  --topbar-h:    52px;
}

/* Role accent colours used in badges and section headers */
--role-guest:   #94a3b8;   /* slate */
--role-client:  #3b82f6;   /* blue */
--role-artist:  #a855f7;   /* purple */
--role-owner:   #22c55e;   /* green */
--role-issuer:  #f59e0b;   /* amber */
```

Light mode via `prefers-color-scheme`:
```css
@media (prefers-color-scheme: light) {
  :root {
    --bg:         #f8fafc;
    --bg-card:    #ffffff;
    --bg-sidebar: #f1f5f9;
    --border:     rgba(0,0,0,.1);
    --text:       #0f172a;
    --text-muted: rgba(15,23,42,.55);
  }
}
```

### Sidebar structure

The sidebar has five role tabs at the top:

```
[ Guest ]  [ Client ]  [ Artist ]  [ Owner ]  [ Issuer ]
```

Each tab reveals a tree of section links for that role. Clicking a link scrolls
the `<main>` to the matching section and highlights it. Active section is tracked
as the user scrolls (`IntersectionObserver`).

### JavaScript behaviour

1. **Hash routing** — clicking a nav item sets `location.hash` and scrolls.
   On page load, scroll to the hash if present. Enables deep-linking.

2. **Search** — `Ctrl+K` or clicking the search icon opens a quick-search modal.
   JS indexes all `<h2>`, `<h3>`, and `.step-title` elements. Results link to
   matching sections. The search is pure substring matching, no library needed.

3. **Section memory** — `localStorage.setItem("lastSection", hash)` saves the
   user's position. On next open (offline or online), the page reopens at the
   same section.

4. **Collapsible sidebar groups** — each role section header is a `<button>`
   that toggles `aria-expanded` + CSS `max-height` transition.

5. **Print** — clicking "Print this section" triggers `window.print()` with a
   `@media print` stylesheet that hides the sidebar and topbar and shows only
   the current `<section>` using `display: none` on all sibling sections via a
   print-only class set by JS before printing.

---

## Phase 4 — Content Specification

Write each section in this exact order. Every section must contain:
1. A `<section id="…">` with a unique ID (for hash linking)
2. An `<h2>` section title with a role badge
3. A "Who can access this" note
4. A brief overview paragraph (2–4 sentences from the code)
5. An SVG wireframe of the screen (see wireframe rules below)
6. Step-by-step numbered workflow
7. Any tips, warnings, or related sections

---

### Wireframe rules

Every SVG wireframe must:
- Be `viewBox="0 0 760 480"` (landscape, proportional to a laptop browser)
- Render the actual nav structure (correct nav items for that role, correct active item)
- Render the correct page title and key UI controls described in the component
- Use approximate positioning — not pixel-perfect, but recognisable
- Label every key element with a `<text>` annotation connected by a `<line>` to the element
- Use `role="img"` and `aria-label="[Screen name] wireframe"` on the `<svg>` tag
- Use the CSS custom property colours (defined as inline `style` attributes in the SVG
  since SVGs inline in HTML do inherit CSS custom properties)

Example wireframe skeleton (adapt per screen):

```svg
<svg viewBox="0 0 760 480" role="img" aria-label="Book Appointment wireframe"
     xmlns="http://www.w3.org/2000/svg" style="width:100%;border-radius:8px;background:#111118">
  <!-- Nav bar -->
  <rect x="0" y="0" width="760" height="44" fill="#0d0d14" rx="0"/>
  <text x="16" y="28" fill="#f4f4f5" font-size="13" font-weight="600">Pena e Artë</text>
  <!-- Nav items -->
  <rect x="160" y="10" width="90" height="24" fill="#7c3aed" rx="6"/>
  <text x="205" y="26" fill="#fff" font-size="11" text-anchor="middle">Book Appt</text>
  <!-- ... more elements ... -->
  <!-- Annotation -->
  <line x1="300" y1="120" x2="360" y2="80" stroke="#7c3aed" stroke-width="1" stroke-dasharray="4"/>
  <text x="365" y="78" fill="#7c3aed" font-size="10">Artist selector</text>
</svg>
```

---

### Manual sections to write

Write every section listed. Pull all content from your Phase 1 reading — do not invent
features that don't exist in the code; do not omit features that do.

#### 0. Introduction

- What is Pena e Artë
- The five roles and their relationship
- How to use this manual
- Platform URL overview

#### 1. Guest / Visitor

| Section ID | Screen | URL |
|---|---|---|
| `guest-discover` | Discover Studios | `/discover` |
| `guest-map` | Studio Map | `/map` |
| `guest-studio-portfolio` | Studio Portfolio | `/s/:slug` |
| `guest-artist-portfolio` | Artist Portfolio | `/artist/:slug` |
| `guest-portfolio-feed` | Global Portfolio Feed | (feed on discover or standalone) |
| `guest-register-client` | Sign Up as a Client | `/client-register` |
| `guest-register-studio` | Register a Studio | `/register` |
| `guest-login` | Sign In | `/login` |
| `guest-forgot-password` | Forgot Password | `/forgot-password` |
| `guest-shared-design` | Viewing a Shared Design Link | `/share/:token` |
| `guest-embed` | Embedded Booking Widget | `/embed/:studioSlug` |

#### 2. Client

| Section ID | Screen | URL |
|---|---|---|
| `client-book` | Book an Appointment | `/book` |
| `client-my-studios` | My Studios + Switching | `/my-studios` |
| `client-designs` | My Designs | `/designs` |
| `client-design-detail` | Design Detail + Approval | `/designs/:id` |
| `client-intake-list` | Intake Forms | `/forms/intake` |
| `client-intake-detail` | Intake Form Detail | `/forms/intake/:id` |
| `client-intake-submit` | Submit Intake Form | `/forms/intake/new` |
| `client-consent-list` | Consent Forms | `/forms/consent` |
| `client-consent-detail` | Consent Form Detail | `/forms/consent/:id` |
| `client-consent-sign` | Sign a Consent Form | `/forms/consent/new` |
| `client-profile` | My Profile + Body Map | `/clients/me` |
| `client-deposit` | Deposit Checkout | `/pay/:paymentId` |
| `client-verify-email` | Verify Email | `/verify-email` |
| `client-change-password` | Change Password | `/account/change-password` |

#### 3. Artist

| Section ID | Screen | URL |
|---|---|---|
| `artist-schedule` | Schedule / Calendar | `/schedule` |
| `artist-appointment-detail` | Appointment Detail | `/appointments/:id` |
| `artist-clients` | Client List (assigned) | `/clients` |
| `artist-client-detail` | Client Detail + Tattoo History | `/clients/:id` |
| `artist-tattoo-record` | Tattoo Record Detail | `/clients/:id/tattoos/:tattooId` |
| `artist-designs` | Designs | `/designs` |
| `artist-create-design` | Create Design | `/designs/new` |
| `artist-design-detail` | Design Detail + Revisions | `/designs/:id` |
| `artist-upload-revision` | Upload Revision | `/designs/:id/upload` |
| `artist-share-design` | Share Design (link) | (button on design detail) |
| `artist-intake-view` | View Intake Forms | `/forms/intake` |
| `artist-consent-view` | View Consent Forms | `/forms/consent` |
| `artist-profile` | My Artist Profile | `/artists/:id` (their own) |
| `artist-notifications` | Notifications | (bell icon in nav) |

#### 4. Studio Owner

| Section ID | Screen | URL |
|---|---|---|
| `owner-dashboard` | Dashboard + Metrics | `/dashboard` |
| `owner-setup-checklist` | Setup Checklist | (card on dashboard) |
| `owner-artists` | Artists List | `/artists` |
| `owner-create-artist` | Create Artist | `/artists/new` |
| `owner-artist-detail` | Artist Detail | `/artists/:id` |
| `owner-clients` | Clients List | `/clients` |
| `owner-create-client` | Create Client | `/clients/new` |
| `owner-client-detail` | Client Detail | `/clients/:id` |
| `owner-designs` | All Studio Designs | `/designs` |
| `owner-design-detail` | Design Detail + Review | `/designs/:id` |
| `owner-deposit-rules` | Deposit Rules | `/deposit-rules` |
| `owner-create-deposit-rule` | Create Deposit Rule | `/deposit-rules/new` |
| `owner-deposit-rule-detail` | Deposit Rule Detail | `/deposit-rules/:id` |
| `owner-schedule` | Schedule View | `/schedule` |
| `owner-appointment-detail` | Appointment Detail | `/appointments/:id` |
| `owner-intake-list` | Intake Forms | `/forms/intake` |
| `owner-intake-detail` | Intake Form Detail | `/forms/intake/:id` |
| `owner-consent-list` | Consent Forms | `/forms/consent` |
| `owner-consent-detail` | Consent Form Detail | `/forms/consent/:id` |
| `owner-payments` | Payments | `/payments` |
| `owner-payment-detail` | Payment Detail | `/payments/:appointmentId` |
| `owner-create-payment` | Create Payment Intent | `/payments/new` |
| `owner-studio-profile` | Studio Profile & Settings | `/studios/me` |
| `owner-branding` | Branding Settings | (card on studio profile) |
| `owner-embed` | Booking Widget Embed Code | (card on studio profile) |
| `owner-referral` | Referral Code | (card on studio profile) |
| `owner-notifications-log` | Notification Log | `/notifications` |
| `owner-notifications-prefs` | Notification Preferences | (card on notification page) |
| `owner-billing` | Billing & Subscription | `/billing` |
| `owner-subscribe` | Subscribe / Change Plan | `/billing/subscribe` |

#### 5. Platform Admin (Issuer)

| Section ID | Screen | URL |
|---|---|---|
| `issuer-dashboard` | Platform Dashboard | `/platform` |
| `issuer-studios` | Studio Oversight | `/platform/studios` |
| `issuer-studio-detail` | Studio Detail | `/platform/studios/:studioId` |
| `issuer-plans` | Plan Management | `/platform/plans` |
| `issuer-subscriptions` | Subscription Oversight | `/platform/subscriptions` |
| `issuer-referrals` | Referral Codes | `/platform/referrals` |
| `issuer-reports` | Industry Reports | `/platform/reports` |
| `issuer-feedback` | Feedback Inbox | `/platform/feedback` |

#### 6. Reference Glossary

| Section ID | Content |
|---|---|
| `ref-design-statuses` | Draft · In Review · Approved · Changes Requested |
| `ref-appointment-statuses` | Pending · Confirmed · Completed · Cancelled |
| `ref-deposit-statuses` | None · Pending · Held · Captured · Refunded |
| `ref-subscription-plans` | Free · Starter · Professional · Enterprise |
| `ref-roles` | Guest · Client · Artist · Owner · Issuer — full comparison table |
| `ref-faq` | 10–15 most common questions derived from the codebase |

---

## Phase 5 — Writing Rules

Follow these rules without exception:

1. **Only describe features that exist in the code you read.** If a component renders
   `canCreate && <Button>`, document that the button only appears for Artist/Owner roles.
   Do not describe features from imagination.

2. **Step numbers must match actual UI flow.** If `BookPage` shows a date picker before
   an artist selector, the steps must be in that order.

3. **Use plain English.** No developer jargon in the manual body. "API", "RTK Query",
   "tenant" should not appear in user-facing text. Use "studio", "profile", "the app".

4. **Role badges** in every section header:
   ```html
   <span class="role-badge role-client">Client</span>
   ```
   Use `role-guest`, `role-client`, `role-artist`, `role-owner`, `role-issuer` CSS classes.

5. **Warning boxes** for destructive actions:
   ```html
   <div class="callout callout-warn">
     <strong>Warning:</strong> Leaving a studio cannot be undone from within the app.
   </div>
   ```

6. **Tip boxes** for shortcuts and power-user features:
   ```html
   <div class="callout callout-tip">
     <strong>Tip:</strong> You can switch studios at any time from My Studios.
   </div>
   ```

7. **Cross-links** between related sections use the hash IDs:
   ```html
   See <a href="#owner-deposit-rules">Deposit Rules</a> for how to configure this.
   ```

8. **Every SVG wireframe** must be preceded by a `<figure>` with a `<figcaption>`.

9. **Each "Who can access this" note** uses a simple table:
   ```html
   <table class="access-table">
     <tr><th>Role</th><th>Access</th></tr>
     <tr><td>Guest</td><td>✗</td></tr>
     <tr><td>Client</td><td>✓</td></tr>
     ...
   </table>
   ```

10. **Language:** Write in English. The app has Albanian UI text in places — translate
    for the manual or note both.

---

## Phase 6 — Future App Integration Notes

At the bottom of the HTML file, add an HTML comment block:

```html
<!--
INTEGRATION NOTES
=================
To embed this manual inside the Pena e Artë React app:

Option A (Static route):
  1. This file is already in frontend/public/user-manual/index.html
  2. It is served at /user-manual/index.html by Vite and the production server
  3. Add a Help button in any layout that opens window.open('/user-manual/index.html')
     or an <a href="/user-manual/index.html" target="_blank"> link

Option B (Iframe panel):
  1. Add a /help route in router.tsx rendering an <iframe> pointed at /user-manual/index.html
  2. Set iframe allow="fullscreen" and sandbox="allow-same-origin allow-scripts allow-popups"
  3. Deep-link to a specific section: /help#owner-dashboard

Option C (In-context help):
  1. Add a HelpButton component to each page header
  2. On click, open a shadcn Sheet or Dialog with:
     <iframe src="/user-manual/index.html#{sectionId}" ... />
  3. Pass the correct sectionId per page (see section IDs in this file's nav)

Offline usage:
  This file has no external dependencies. It can be:
  - Saved locally and opened from disk
  - Bundled into a desktop Electron/Tauri shell
  - Cached via a service worker for offline PWA access:
    self.addEventListener('fetch', (e) => {
      if (e.request.url.endsWith('/user-manual/index.html')) {
        e.respondWith(caches.match(e.request) || fetch(e.request));
      }
    });
-->
```

---

## Phase 7 — Verification

After generating the file, verify:

1. Open `frontend/public/user-manual/index.html` in a browser (from disk — no server).
   All navigation must work. All sections must be reachable.

2. Check that no `<link href="...">` or `<script src="...">` tags reference external URLs.
   Run: `grep -E "(https?://|cdn\.|fonts\.)" user-manual/index.html`
   Result must be empty.

3. Confirm the file opens without any console errors when JavaScript is enabled.

4. Confirm all 5 role tab sections are present in the sidebar nav.

5. Confirm every section listed in Phase 4 has an `<h2>` with the correct `id` attribute.
   Run: `grep -oE 'id="[a-z-]+"' user-manual/index.html | sort` and cross-check against
   the section table.

6. File size: `ls -lh frontend/public/user-manual/index.html`. Must be under 600 KB.
   If over, compress SVG paths, shorten prose, remove redundant HTML.

---

## Exit Condition

Verification passes. Then:

1. Create `frontend/public/user-manual/` directory if it doesn't exist.

2. Save the complete HTML to `frontend/public/user-manual/index.html`.

3. Append to `docs/claude/architecture.md`:

```markdown
## User Manual — 2026-07-04

A single self-contained offline HTML manual covering all five roles.

- File: `frontend/public/user-manual/index.html`
- URL (dev): `http://localhost:5173/user-manual/index.html`
- No external deps — fully offline capable
- Covers: Guest (11 sections) · Client (14) · Artist (14) · Owner (28) · Issuer (8) · Glossary (6)
- Integration: see the HTML comment block at the bottom of the file for embed options
- Section IDs follow the pattern `{role}-{feature}` for deep-linking
```
