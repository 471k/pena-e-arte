# Overnight Prompt — Basic Mobile UI/UX Baseline (Nav Drawer + Responsive Tables, All 4 Roles)

> Feed this file directly to Claude Code as the task prompt, in the main
> **"Pena e Artë - Engineering"** project (the one with repo write access —
> this file was produced in the separate, read-only "Engineering Consultation"
> project and cannot touch source itself). It is self-contained: exact files,
> exact current code, exact target code, exact tests, exact docs to sync. Read
> the whole file before writing anything. Mode: fully autonomous, no user
> present.

**Date logged:** 2026-08-20
**Requested by:** Phi
**Origin:** `spec-plan-basic-mobile-ui-ux.md` (this consultation project,
2026-08-20), which audited all four role layouts, the shared `DataTable`
primitive, and the onboarding-tour targeting mechanism against the live
source. Two scope decisions from that spec plan were confirmed by Phi before
this prompt was written: (1) use one Sheet-based drawer nav for all four
roles, not a bottom tab bar for the client role; (2) migrate all three
`DataTable`-based list pages to the new mobile card view in this same run,
not as a follow-up. A third open question from the spec plan — whether a
`useMediaQuery`-style hook already exists — is resolved below in §2: it
doesn't, and this prompt deliberately avoids adding one, using a CSS-only
dual-render approach instead (matches how every other responsive behavior in
this codebase already works — no JS viewport hook exists anywhere in
`shared/hooks/`, confirmed by `grep -rln "matchMedia\|useMediaQuery" frontend/src`
returning nothing).

**Before starting, run:**
```bash
git add -A && git commit -m "checkpoint: before basic-mobile-ui-ux overnight prompt" --allow-empty
git checkout -b feat/mobile-ui-ux-baseline
```

---

## 1. Goal

Replace the horizontally-scrolling icon+label nav strip shared by all four
role layouts (`ClientLayout`, `ArtistLayout`, `OwnerLayout`, `IssuerLayout`)
with a hamburger-triggered drawer below the `lg` breakpoint, and give the
shared `DataTable` component (used by `ArtistListPage`, `ClientListPage`,
`PaymentListPage`) a responsive card fallback below `sm`. This is a
navigation-architecture and shared-component fix, not a redesign of any
individual page's content — every route, label, and piece of data stays
exactly where it is.

Applicable `CLAUDE.md` rules: #6 (industry benchmark — §7), #7 (Help sync —
§6, including a real onboarding-tour behavior change, not just copy).

---

## 2. Decisions already made — do not re-litigate

These were open questions in the source spec plan; Phi resolved the first two,
this prompt resolves the third with a concrete technical reason:

1. **Nav pattern: Sheet-based drawer, all four roles, no bottom tab bar.**
   Reuses `shared/components/ui/sheet.tsx` (already proven on mobile via
   `StudioNotificationSheet`). One pattern for Client/Artist/Owner/Issuer.
2. **List-page migration included in this run.** `ArtistListPage`,
   `ClientListPage`, `PaymentListPage` all get a `mobileCard` prop in this
   same pass — §4.4.
3. **No new `useMediaQuery` hook.** `DataTable`'s responsive behavior is
   CSS-only dual-render: both the card list and the table are written to the
   DOM when a `mobileCard` prop is supplied, toggled with Tailwind's
   `sm:hidden` / `hidden sm:block`, exactly like `ArtistPortfolioPage.tsx`'s
   existing `lg:hidden` sticky Book CTA and `ClientLayout.tsx`'s existing
   `hidden sm:inline` label swap. No `matchMedia`, no new hook, no
   hydration-mismatch risk. Same technique is used for `NavDrawer`'s
   trigger button (`lg:hidden`) and the desktop nav (`hidden lg:flex`).

---

## 3. Scope boundary — do not touch

- Route definitions in `app/router.tsx` — no route, path, or role-guard
  changes. This is purely how nav items are *presented*, not what they point
  to.
- `helpContent.ts` article *content* beyond what §6 specifies — no new
  articles, no route/feature descriptions changed.
- `frontend/public/user-manual/index.html` beyond the specific tour-adjacent
  note in §6 — this prompt does not do a manual-wide pass.
- Any table/list page other than `ArtistListPage`, `ClientListPage`,
  `PaymentListPage` — `HelpInsightsPage.tsx`'s existing `overflow-x-auto`
  table and any other bespoke (non-`DataTable`) table elsewhere are out of
  scope; they were not flagged as broken and use a different component.
- Issuer action-button `flex-wrap` fixes from the 2026-07-20/21
  industry-parity audit (F12) — already shipped, not re-touched here.
- Any backend file — this is a frontend-only prompt. No entity, migration,
  endpoint, or handler changes anywhere.
- Desktop (`≥ lg`) visual behavior of any of the four layouts — the existing
  horizontal nav keeps rendering exactly as today at `lg` and above; only
  its wrapping `className` changes from unconditional to `hidden lg:flex`.
- `FeedbackDialog`, `StudioNotificationSheet`, and `HelpMenu`'s own `Sheet` —
  unrelated overlays, not modified. (One pre-existing edge case, not
  introduced by this prompt and not fixed here: if a user opens the nav
  drawer and then also opens `HelpMenu`'s sheet, both render at `z-50` with
  no explicit stacking order between them — same latent ambiguity already
  exists today between `HelpMenu`'s sheet and `FeedbackDialog`. Not addressed
  in this pass.)

---

## 4. Frontend changes — exact files, current code, target code

### 4.1 New shared type + component: `NavItem` and `NavDrawer`

**New file: `frontend/src/shared/types/navItem.ts`**
```typescript
import type { ReactNode } from "react";

export interface NavItem {
  label:      string;
  href:       string;
  icon:       ReactNode;
  tourId?:    string;
  end?:       boolean;   // exact-match routing, e.g. IssuerLayout's Dashboard item
  badge?:     number;    // e.g. IssuerLayout's open-feedback count
}
```

**New file: `frontend/src/shared/components/NavDrawer.tsx`**
```tsx
import { NavLink } from "react-router-dom";
import { Menu } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle, SheetClose,
} from "@/shared/components/ui/sheet";
import { cn } from "@/shared/utils/cn";
import type { NavItem } from "@/shared/types/navItem";

interface NavDrawerProps {
  navItems: NavItem[];
  title: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NavDrawer({ navItems, title, open, onOpenChange }: NavDrawerProps) {
  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        className="h-8 w-8 lg:hidden"
        aria-label="Open navigation menu"
        onClick={() => onOpenChange(true)}
      >
        <Menu className="h-5 w-5" />
      </Button>

      <Sheet open={open} onOpenChange={onOpenChange}>
        <SheetContent side="left" className="w-72 flex flex-col gap-1 overflow-y-auto">
          <SheetHeader>
            <SheetTitle>{title}</SheetTitle>
          </SheetHeader>
          <nav className="flex flex-col gap-1 mt-2" aria-label="Main navigation">
            {navItems.map(({ label, href, icon, tourId, end, badge }) => (
              <SheetClose asChild key={href}>
                <NavLink
                  to={href}
                  end={end}
                  data-tour={tourId}
                  className={({ isActive }) =>
                    cn(
                      "flex items-center gap-3 px-3 min-h-[44px] rounded-md text-sm transition-colors",
                      isActive
                        ? "bg-violet-600 text-white"
                        : "text-muted-foreground hover:text-foreground hover:bg-muted",
                    )
                  }
                >
                  {icon}
                  <span>{label}</span>
                  {!!badge && badge > 0 && (
                    <span className="ml-auto min-w-[1.25rem] rounded-full bg-destructive px-1 py-0.5 text-[10px] font-medium text-destructive-foreground text-center">
                      {badge > 99 ? "99+" : badge}
                    </span>
                  )}
                </NavLink>
              </SheetClose>
            ))}
          </nav>
        </SheetContent>
      </Sheet>
    </>
  );
}
```
Notes:
- `SheetClose asChild` wrapping each `NavLink` is the standard Radix pattern
  for "this link both navigates and closes the sheet" — no manual
  `onOpenChange(false)` call needed in the click handler.
- `min-h-[44px]` on every item satisfies the 44px touch-target minimum from
  the spec plan directly in the shared component, so no per-layout
  touch-target patching is needed (this supersedes the spec plan's earlier
  idea of bumping the old pills' `py-1.5` — see §4.2, `ClientLayout` cleanup).
- Badge rendering matches `IssuerLayout.tsx`'s existing inline badge markup
  (lines 59–63) exactly, so the drawer's badge is visually identical to the
  one already shown on desktop.

### 4.2 Wire `NavDrawer` into all four layouts

For each layout: add `const [navOpen, setNavOpen] = useState(false);`
(already imports `useState` in `ArtistLayout`/`OwnerLayout`; add the import
to `ClientLayout`/`IssuerLayout`, which currently don't have it). Change the
existing `<nav>` wrapper to `hidden lg:flex` (was unconditional), and render
`<NavDrawer navItems={...} title="..." open={navOpen} onOpenChange={setNavOpen} />`
immediately after it, before the `<div className="ml-auto ...">` block.
Convert each layout's `NAV_ITEMS`/`STATIC_NAV` array to the shared `NavItem[]`
shape (drop `shortLabel` — dead once the drawer replaces mobile nav
presentation, see cleanup below).

**`frontend/src/layouts/ClientLayout.tsx`** — current `NAV_ITEMS` (lines
16–23) has a `shortLabel` field and the `NavLink`'s `className`/children use
`py-2.5 sm:py-1.5` and `hidden sm:inline` / `sm:hidden` label-swap logic
(lines 53, 62–63) — this was the one place the spec plan's touch-target fix
was ever applied, and it's now fully superseded by `NavDrawer` owning its
own `min-h-[44px]`. Remove it; this is cleanup, not a regression, since the
drawer is what mobile/tablet users see now, not this row.

Target:
```tsx
const NAV_ITEMS: NavItem[] = [
  { label: "Book Appointment", href: "/book",        icon: <CalendarDays className="h-4 w-4" />, tourId: "client-book-nav" },
  { label: "My Studios",       href: "/my-studios",  icon: <Building2    className="h-4 w-4" />, tourId: "client-my-studios-nav" },
  { label: "My Designs",       href: "/designs",       icon: <Palette      className="h-4 w-4" />, tourId: "client-designs-nav" },
  { label: "Intake Forms",     href: "/forms/intake",  icon: <FileText     className="h-4 w-4" /> },
  { label: "Consent Forms",    href: "/forms/consent", icon: <ScrollText   className="h-4 w-4" /> },
  { label: "My Profile",       href: "/clients/me",    icon: <User         className="h-4 w-4" /> },
];
```
```tsx
<nav className="hidden lg:flex ml-6 items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
  {NAV_ITEMS.map(({ label, href, icon, tourId }) => (
    <NavLink
      key={href}
      to={href}
      data-tour={tourId}
      className={({ isActive }) =>
        cn(
          "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors shrink-0",
          isActive ? "bg-violet-600 text-white" : "text-muted-foreground hover:text-foreground hover:bg-muted",
        )
      }
      aria-label={label}
    >
      {icon}
      {label}
    </NavLink>
  ))}
</nav>
<NavDrawer navItems={NAV_ITEMS} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} />
```
Add `import { useState } from "react";`, `import { NavDrawer } from "@/shared/components/NavDrawer";`, `import type { NavItem } from "@/shared/types/navItem";`.

**`frontend/src/layouts/ArtistLayout.tsx`** — current `STATIC_NAV` (lines
21–29) plus the conditional "My Portfolio" item (lines 73–88, only rendered
`if (myArtist)`). Build the full `NavItem[]` array once, conditionally
including the portfolio item, and pass that single array to both the
desktop `<nav>` map and `NavDrawer` — do not duplicate the conditional
between two separate render blocks.

Target (replace `STATIC_NAV` and the inline conditional portfolio `NavLink`):
```tsx
const STATIC_NAV: NavItem[] = [
  { label: "Schedule",      href: "/schedule",      icon: <CalendarDays className="h-4 w-4" />, tourId: "artist-schedule-nav" },
  { label: "Clients",       href: "/clients",       icon: <Users        className="h-4 w-4" />, tourId: "artist-clients-nav" },
  { label: "Designs",       href: "/designs",       icon: <Palette      className="h-4 w-4" /> },
  { label: "Intake Forms",  href: "/forms/intake",  icon: <FileText     className="h-4 w-4" /> },
  { label: "Consent Forms", href: "/forms/consent", icon: <ScrollText   className="h-4 w-4" /> },
  { label: "Deposit Rules", href: "/deposit-rules", icon: <DollarSign   className="h-4 w-4" /> },
  { label: "Notifications", href: "/notifications", icon: <Bell         className="h-4 w-4" /> },
];
```
Inside the component, after `const { data: myArtist } = useGetMyArtistQuery();`:
```tsx
const navItems: NavItem[] = myArtist
  ? [...STATIC_NAV, { label: "My Portfolio", href: `/artists/${myArtist.id}`, icon: <ImagePlus className="h-4 w-4" /> }]
  : STATIC_NAV;
```
Desktop `<nav>` becomes `className="hidden lg:flex ml-6 items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0"`, maps over `navItems` instead of `STATIC_NAV` + the separate conditional block (delete the separate `{myArtist && (...)}` block entirely — it's now part of the array). Add `<NavDrawer navItems={navItems} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} />` after the `</nav>`.

**`frontend/src/layouts/OwnerLayout.tsx`** — current `NAV_ITEMS` (lines
22–32), no conditional items. Same mechanical conversion: `NavItem[]` typed
array (drop nothing, no `shortLabel` was ever present here), `<nav>` becomes
`hidden lg:flex ...`, add `<NavDrawer navItems={NAV_ITEMS} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} />`.

**`frontend/src/layouts/IssuerLayout.tsx`** — current `NAV_ITEMS` (lines
11–21), no `useState` import yet (add it), and the Feedback item's inline
badge (lines 59–63) needs to move into the `NavItem[]` array as
`badge: openCount` rather than a JSX conditional inside `.map()`, so
`NavDrawer` renders the same badge without duplicating the render logic.

Target:
```tsx
const NAV_ITEMS: NavItem[] = [
  { label: "Dashboard",     href: "/platform",               icon: <LayoutDashboard className="h-4 w-4" />, tourId: "issuer-dashboard-nav", end: true },
  { label: "Live Traffic",  href: "/platform/traffic",       icon: <Activity        className="h-4 w-4" />, tourId: "issuer-traffic-nav" },
  { label: "Studios",       href: "/platform/studios",       icon: <Building2       className="h-4 w-4" />, tourId: "issuer-studios-nav" },
  { label: "Plans",         href: "/platform/plans",         icon: <CreditCard      className="h-4 w-4" />, tourId: "issuer-plans-nav" },
  { label: "Subscriptions", href: "/platform/subscriptions", icon: <Receipt         className="h-4 w-4" />, tourId: "issuer-subscriptions-nav" },
  { label: "Referrals",     href: "/platform/referrals",     icon: <Share2          className="h-4 w-4" /> },
  { label: "Reports",       href: "/platform/reports",       icon: <BarChart3       className="h-4 w-4" /> },
  { label: "Feedback",      href: "/platform/feedback",      icon: <MessageSquare   className="h-4 w-4" /> },
  { label: "Help Insights", href: "/platform/help-insights", icon: <HelpCircle      className="h-4 w-4" /> },
  { label: "Audit Log",     href: "/platform/audit-log",     icon: <ScrollText      className="h-4 w-4" />, tourId: "issuer-audit-log-nav" },
];
```
Note `end: true` moved from the JSX-only `end={href === "/platform"}` conditional into the data itself — cleaner than re-deriving it in two render sites. Inside the component: `const navItems: NavItem[] = NAV_ITEMS.map((item) => item.label === "Feedback" ? { ...item, badge: openCount } : item);` (computed after `openCount` is known). Desktop `<nav>` maps `navItems` (drop the inline `{label === "Feedback" && openCount > 0 && (...)}` block, replaced by `NavDrawer`'s and the desktop nav's shared `badge` field — desktop `<nav>`'s own `NavLink` also needs the same badge-render snippet as `NavDrawer` since it's a separate render site; keep it, just source the value from `item.badge` instead of the standalone `openCount` variable check). Add `import { useState } from "react";`, `NavDrawer`, `NavItem` imports as above.

### 4.3 `DataTable.tsx` — responsive card fallback

**File:** `frontend/src/shared/components/DataTable.tsx`

Current (full file, reproduced from §2 of the source spec plan): bare
`<Table>` render, no wrapper, no mobile handling.

Target:
```tsx
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "./ui/table";
import { cn } from "@/shared/utils/cn";

export interface ColumnDef<T> {
  header: string;
  accessorKey?: keyof T;
  cell?: (row: T) => React.ReactNode;
}

interface DataTableProps<T> {
  columns: ColumnDef<T>[];
  data: T[];
  keyExtractor: (row: T) => string;
  onRowClick?: (row: T) => void;
  emptyMessage?: string;
  /** When provided, rows render as stacked cards below the `sm` breakpoint
   *  instead of the table. Omit to keep the existing table-only behavior
   *  (now wrapped in `overflow-x-auto` so it never regresses on narrow
   *  screens even before a page adopts the card view). */
  mobileCard?: (row: T) => React.ReactNode;
}

export function DataTable<T>({
  columns,
  data,
  keyExtractor,
  onRowClick,
  emptyMessage = "No results.",
  mobileCard,
}: DataTableProps<T>) {
  const showCards = !!mobileCard && data.length > 0;

  return (
    <>
      {showCards && (
        <div className="sm:hidden flex flex-col gap-2" role="list">
          {data.map((row) => (
            <div
              key={keyExtractor(row)}
              role="listitem"
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={cn(
                "rounded-lg border p-3",
                onRowClick && "cursor-pointer active:bg-muted",
              )}
            >
              {mobileCard!(row)}
            </div>
          ))}
        </div>
      )}

      <div className={cn("overflow-x-auto", showCards && "hidden sm:block")}>
        <Table>
          <TableHeader>
            <TableRow>
              {columns.map((col) => (
                <TableHead key={col.header}>{col.header}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.length === 0 ? (
              <TableRow>
                <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
                  {emptyMessage}
                </TableCell>
              </TableRow>
            ) : (
              data.map((row) => (
                <TableRow
                  key={keyExtractor(row)}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  className={onRowClick ? "cursor-pointer" : undefined}
                >
                  {columns.map((col) => (
                    <TableCell key={col.header}>
                      {col.cell
                        ? col.cell(row)
                        : col.accessorKey
                        ? String(row[col.accessorKey] ?? "")
                        : null}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>
    </>
  );
}
```
Important edge case, handled deliberately: when `data.length === 0`, `showCards`
is `false` regardless of whether `mobileCard` was passed, so the table's own
existing `colSpan` empty-message row renders at all widths — there is no
"empty mobile view with nothing in it" gap. Only non-empty data ever triggers
the dual-render split.

### 4.4 Migrate the three list pages to `mobileCard`

**`frontend/src/features/artists/components/ArtistListPage.tsx`** — add a
`mobileCard` prop to the existing `<DataTable<ArtistResponse>>` call
(line ~309), reusing the same closures (`navigate`, `canManage`,
`confirmDeleteId`, `setConfirmDeleteId`, `deleteArtist`, `isDeletingArtist`)
already in scope for the `columns` array — do not duplicate the delete-confirm
state machine, call the same handlers:
```tsx
mobileCard={(a) => (
  <div className="space-y-2" onClick={(e) => { if ((e.target as HTMLElement).closest("[data-card-actions]")) e.stopPropagation(); }}>
    <div className="flex items-center gap-2">
      <div className="h-8 w-8 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0 select-none">
        {a.firstName[0]?.toUpperCase()}{a.lastName[0]?.toUpperCase()}
      </div>
      <div className="min-w-0">
        <p className="font-medium truncate">{a.firstName} {a.lastName}</p>
        <p className="text-xs text-muted-foreground truncate">{a.email}</p>
      </div>
    </div>
    {a.specializations && (
      <div className="flex flex-wrap gap-1">
        {a.specializations.split(",").map((s) => s.trim()).filter(Boolean).map((spec) => (
          <span key={spec} className="rounded-full bg-muted px-1.5 py-0.5 text-xs font-medium">{spec}</span>
        ))}
      </div>
    )}
    <div data-card-actions className="flex items-center justify-end gap-1 pt-1">
      <Button variant="ghost" size="sm" className="h-8 text-xs gap-1" onClick={() => navigate(`/artists/${a.id}`)}>
        <Pencil className="h-3.5 w-3.5" /> Edit
      </Button>
      {canManage && (
        confirmDeleteId === a.id ? (
          <div className="flex items-center gap-1.5">
            <Button variant="ghost" size="sm" className="h-8 text-xs" onClick={() => setConfirmDeleteId(null)}>Cancel</Button>
            <Button variant="destructive" size="sm" className="h-8 text-xs" disabled={isDeletingArtist} onClick={async () => {
              try { await deleteArtist(a.id).unwrap(); toast.success("Artist deleted."); }
              catch (err: unknown) {
                const message = (err as { data?: { message?: string } } | undefined)?.data?.message ?? "Failed to delete artist.";
                toast.error(message);
              }
              setConfirmDeleteId(null);
            }}>
              {isDeletingArtist ? "Deleting…" : "Confirm"}
            </Button>
          </div>
        ) : (
          <Button variant="ghost" size="sm" className="h-8 text-xs gap-1 text-destructive hover:text-destructive hover:bg-destructive/10" onClick={() => setConfirmDeleteId(a.id)}>
            <Trash2 className="h-3.5 w-3.5" /> Delete
          </Button>
        )
      )}
    </div>
  </div>
)}
```
Note the `data-card-actions` + `closest()` guard: the card itself is
`onRowClick`-navigable (per `DataTable`'s new card wrapper), but the
Edit/Delete buttons must not also trigger row navigation — same problem the
existing desktop `columns` cell already solves with `onClick={(e) => e.stopPropagation()}`
on its wrapping `<div>` (line ~140); the card needs an equivalent guard since
it's a single flat `onClick` on the outer card `div` rather than per-cell.

**`frontend/src/features/clients/components/ClientListPage.tsx`** — add
`mobileCard` to the existing `<DataTable<ClientResponse>>` call:
```tsx
mobileCard={(c) => (
  <div className="flex items-center gap-2">
    <div className="h-8 w-8 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0 select-none">
      {c.firstName[0]?.toUpperCase()}{c.lastName[0]?.toUpperCase()}
    </div>
    <div className="min-w-0 flex-1">
      <p className="font-medium truncate">{c.firstName} {c.lastName}</p>
      <p className="text-xs text-muted-foreground truncate">{c.email}{c.phone ? ` · ${c.phone}` : ""}</p>
    </div>
    <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
  </div>
)}
```
No action-button guard needed — the whole card is a plain navigate-on-tap
row, matching this page's existing "View" chevron affordance (the chevron is
now decorative/non-interactive on the card, same as it's non-interactive
today next to the "View" text on desktop).

**`frontend/src/features/payments/components/PaymentListPage.tsx`** — add
`mobileCard` to the existing `<DataTable<PaymentResponse>>` call:
```tsx
mobileCard={(p) => (
  <div className="space-y-1">
    <div className="flex items-center justify-between gap-2">
      <span className="text-sm font-medium truncate">{p.clientName || "—"}</span>
      <PaymentStatusBadge status={p.status} />
    </div>
    <div className="flex items-center justify-between gap-2 text-sm">
      <span className="text-muted-foreground">
        {p.appointmentDate ? formatDate(p.appointmentDate) : "—"}
      </span>
      <span className="font-semibold">{formatCurrency(p.amount)}</span>
    </div>
    <p className="text-xs text-muted-foreground">
      {p.method}{p.paidAt ? ` · Paid ${formatDate(p.paidAt)}` : ""}
    </p>
  </div>
)}
```
Reuses the page's existing `PaymentStatusBadge`, `formatDate`, `formatCurrency`
— no new formatting logic.

### 4.5 Onboarding-tour fix: nav steps must open the drawer first

**Root cause, verified by reading `OnboardingTour.tsx`:** `document.querySelector(step.targetSelector)`
(lines 46, 90, 95) is how every tour step finds its target. `SheetContent`
(Radix `DialogPrimitive.Content`) is not rendered into the DOM at all while
its `Sheet` is closed — no `forceMount` is used anywhere in this codebase's
`sheet.tsx`. Once §4.1–4.2 move every `data-tour="*-nav"` element into the
closed-by-default `NavDrawer`, `document.querySelector('[data-tour="client-book-nav"]')`
etc. will find nothing. The polling loop (`MAX_POLL_ATTEMPTS = 20`,
`POLL_INTERVAL_MS = 50`, ~1s total) will exhaust and call
`skipUnresolvableStep()` (line 51), which silently advances past the step.
**Without this fix, every nav-targeting tour step silently disappears on any
viewport below `lg`** — 3 of `clientTour`'s 4 steps, 2 of `artistTour`'s 5,
4 of `ownerTour`'s 7, 4 of `issuerTour`'s 7 (all of them ending in `-nav"]`
per the existing naming convention — no tour-file changes needed to detect
this, the selector suffix is already consistent).

**File: `frontend/src/shared/components/OnboardingTour.tsx`** — add an
optional callback, invoked once per step transition, before measurement
starts:
```tsx
export interface OnboardingTourProps {
  steps: TourStep[];
  onComplete: () => void;
  onSkip: () => void;
  /** Called once per step, before the target element is searched for —
   *  use this to open a container (e.g. a mobile nav drawer) that the
   *  step's target may be hidden inside. */
  onBeforeStep?: (step: TourStep) => void;
}
```
In the component signature: `export function OnboardingTour({ steps, onComplete, onSkip, onBeforeStep }: OnboardingTourProps) {`.
At the top of the target-resolution `useEffect` (line 37), right after
`if (!step) return;`:
```tsx
onBeforeStep?.(step);
```
(Placed before the `setTargetRect(null)` line and the route-navigate branch —
runs unconditionally on every step, including the very first.)

**File: `frontend/src/features/help/useOnboardingTour.tsx`** — thread the
callback through:
```tsx
export function useOnboardingTour(role: Role | null, onBeforeStep?: (step: TourStep) => void) {
  // ...unchanged body...
  const tourElement = shouldShow && role
    ? <OnboardingTour steps={steps} onComplete={finish} onSkip={finish} onBeforeStep={onBeforeStep} />
    : null;

  return { tourElement, restartTour };
}
```

**File: `frontend/src/features/help/components/HelpMenu.tsx`** — accept the
same optional callback as a prop and pass it through to `useOnboardingTour`:
```tsx
interface HelpMenuProps {
  onBeforeTourStep?: (step: TourStep) => void;
}

export function HelpMenu({ onBeforeTourStep }: HelpMenuProps) {
  // ...
  const { tourElement, restartTour } = useOnboardingTour(role as Role | null, onBeforeTourStep);
  // ...
}
```
Add `import type { TourStep } from "@/shared/components/OnboardingTour";`.

**Each of the four layouts** — pass a callback that opens/closes the
already-added `navOpen` state based on whether the step's target lives in the
drawer, using the existing `-nav"]` selector-suffix convention (already
consistent across all four tour files, verified in §4.5's root-cause note
above — no tour-file edits needed):
```tsx
<HelpMenu onBeforeTourStep={(step) => setNavOpen(step.targetSelector.endsWith('-nav"]'))} />
```
This both opens the drawer before a nav step is measured and closes it again
the moment the tour advances to a non-nav step (e.g. the help button), so the
tour never leaves the drawer stuck open on an unrelated step.

---

## 5. Test requirements

**New: `frontend/src/shared/components/__tests__/NavDrawer.test.tsx`**
- Hamburger trigger renders and is `lg:hidden` (class-presence assertion,
  matching this codebase's existing convention for other responsive-class
  checks).
- Clicking the trigger opens the `Sheet` (`SheetTitle` text becomes visible).
- All `navItems` render inside the open drawer, each `NavLink` has
  `min-h-[44px]` in its className.
- Clicking a `NavLink` inside the drawer both calls the router navigation
  (assert via `MemoryRouter` location change, matching how other nav tests
  in this codebase assert navigation) and closes the sheet (`SheetTitle` no
  longer in the document).
- Badge renders when `badge > 0`, does not render when `badge` is `0`/`undefined`.
- Drawer is controllable from outside: rendering with `open={true}` shows it
  without a click.

**New: `frontend/src/shared/components/__tests__/DataTable.test.tsx`** (check
whether a test file already exists for `DataTable` before assuming — if one
exists, extend it, don't duplicate the setup):
- Without `mobileCard`: table renders wrapped in a `overflow-x-auto` div
  (class assertion), no card list rendered.
- With `mobileCard` and non-empty `data`: both the card list (`role="list"`)
  and the table wrapper (`hidden sm:block`) are present in the DOM
  simultaneously (this is the CSS-dual-render design — assert both exist,
  do not assert visibility, since jsdom doesn't apply the stylesheet that
  makes `hidden`/`sm:block` actually toggle display).
- With `mobileCard` and empty `data`: card list is *not* rendered
  (`showCards` false path), table's own `emptyMessage` row is present and
  not wrapped in `hidden sm:block`.
- `onRowClick` fires from both a card `listitem` click and a table row click.

**Extend `frontend/src/features/artists/__tests__/artists.test.tsx` /
`clients/__tests__/ClientListPage.test.tsx` /
`payments/__tests__/*PaymentListPage*.test.tsx`** (read each file's existing
fixture/mock setup first, add alongside, don't restructure): assert the new
`mobileCard` content renders (e.g. `getAllByText` for a client/artist/payment
name now appears twice — once in the card, once in the table — since both
render simultaneously per the dual-render design; scope queries with
`within()` against the card container vs. the table where a test needs to
distinguish them, matching how `getAllByText`/`within` are already used
elsewhere in this suite if applicable).

**Extend all four `frontend/src/layouts/__tests__/*Layout.test.tsx` files:**
- Old assertions that queried nav item text directly at the top level still
  pass unchanged (items still render in the desktop `<nav>`, now wrapped in
  `hidden lg:flex` — a class change only, elements are still in the DOM in
  jsdom, matching this test file's existing query style).
- New: hamburger `NavDrawer` trigger is present in the header.
- New: opening the drawer and clicking a nav item navigates and closes it
  (reuse the same `MemoryRouter`/`Provider` scaffolding already in each
  file's `renderLayout`-style helper — check each file's existing helper
  name before assuming `renderLayout`).
- `IssuerLayout.test.tsx` specifically: badge count (`openCount`) appears
  both on the desktop nav's Feedback item and inside the drawer's Feedback
  item when opened.

**Extend `OnboardingTour.test.tsx`** (find the existing test file under
`shared/components/__tests__/` — read it first to match its `TourStep`
fixture style):
- New test: `onBeforeStep` is called once per step transition, including
  step 0, before `document.querySelector` would need to resolve — assert via
  a mock function call count and call order relative to when the target
  becomes measurable (e.g. render a step whose target is added to the DOM
  only after `onBeforeStep` fires, matching the drawer's real open-then-mount
  timing, and confirm the step still resolves instead of being skipped).

**No backend tests** — this prompt makes no backend changes.

---

## 6. Help-sync obligations (per change, not an appendix)

1. **Nav item containers moving (drawer vs. inline) — no `helpContent.ts` or
   standalone-manual content change.** Verified: neither file describes the
   nav's visual layout (horizontal strip vs. drawer) anywhere — both only
   reference nav items by name/purpose ("use the sidebar," "the X tab," etc.,
   scan confirmed no literal "scroll" or "swipe" nav instructions exist to go
   stale). No route, label, or capability changed. Stated explicitly per this
   project's own rule that a "no" verdict must be justified, not silently
   skipped.

2. **Onboarding tour (§4.5) — real behavior fix, not a copy change, but no
   new Help *content* is needed.** The fix makes existing tour steps work
   again on narrow viewports (they'd otherwise silently vanish); it doesn't
   add, remove, or reword any step, article, or manual section. No
   `helpContent.ts`/manual edit required for this specifically — but this is
   the single most important regression this prompt prevents, so it must not
   be skipped or treated as optional polish; it's load-bearing for rule #7's
   own spirit (a broken tour is a Help surface silently going stale).

3. **`DataTable` card view (§4.3–4.4) — no Help change.** Same data, same
   routes, same actions, different presentation at narrow widths only. No
   existing Help content describes table layout mechanics to go stale.

4. **`docs/claude/conventions.md`** — add a new section (this is documentation
   this consultation project asked for in the source spec plan, item 7 of
   "What needs to change" — include it in this same overnight run since it's
   a trivial doc addition, not a separate follow-up):
   ```markdown
   ---

   ## Mobile / Responsive Conventions

   Breakpoints: use Tailwind's default `sm` (640px) and `lg` (1024px) tokens
   only — do not add custom breakpoints to `index.css`'s `@theme`.

   - Below `sm`: phone. Stacked/card layouts, full-width controls.
   - `sm`–`lg`: tablet/narrow desktop. Most pages behave like desktop but
     dense controls (nav, action-button rows) still collapse.
   - `lg`+: desktop. No special-casing needed beyond what's already there.

   Touch targets: minimum 44×44px hit area for any tappable element below
   `sm` (buttons, nav items, icon-only actions, table row actions). Above
   `sm`, denser desktop sizing is fine.

   Navigation: use `shared/components/NavDrawer.tsx` (a `Sheet`-based drawer,
   `lg:hidden`) for any role layout's primary nav — do not build a new
   off-canvas/hamburger pattern. Desktop (`lg`+) nav stays a plain horizontal
   `<nav>`, wrapped `hidden lg:flex`.

   Tables: use `DataTable`'s `mobileCard` prop for any list of records with
   more than ~3 columns — do not ship a bare `<Table>` for tabular data meant
   to be usable on a phone. `DataTable` without `mobileCard` still gets an
   `overflow-x-auto` wrapper automatically, so it never regresses, but a
   horizontally-scrolled dense table is a fallback, not a target state.
   ```

---

## 7. Industry-standard benchmark note

Per `CLAUDE.md` rule #6, vertical booking-SaaS set (Fresha, Vagaro,
Boulevard, Mindbody, Zenoti, GlossGenius, Booksy, Mangomint):

- A collapsible/drawer nav below tablet width is the near-universal pattern
  across this benchmark set's staff- and owner-facing web apps — none of
  them ship a raw horizontally-scrolling icon+label strip as primary
  navigation on a phone-width viewport.
- None of the benchmark set ships a data table for staff-facing record lists
  (clients, staff, transactions) without either a responsive card/list view
  or, at minimum, a visibly-scrollable wrapper — a bare unwrapped `<table>`
  that clips or squishes at 375px is below the category floor, not just
  behind the leading edge of it.
- A 44px (iOS HIG) / 48dp (Material) touch-target minimum is standard
  practice, not aspirational, across any production mobile-web product in
  this category or adjacent ones.

This prompt brings the app to that baseline. It does not attempt
native-app-tier polish (swipe gestures, pull-to-refresh, offline support) —
those would be separate, larger specs, consistent with the source spec
plan's own scoping.

---

## 8. Constraints (restated in full, as required)

- **No new npm/NuGet packages.** `NavDrawer` reuses `shared/components/ui/sheet.tsx`
  (Radix, already a dependency) and `lucide-react`'s `Menu` icon (already
  imported elsewhere in this codebase). `DataTable`'s change is pure
  React/Tailwind. No `useMediaQuery`/`matchMedia` library added — see §2.
- **No `useEffect` for data fetching** — nothing in this prompt fetches data;
  it's presentation-only.
- **TypeScript strict, no `any`** — `NavItem` is a fully-typed interface;
  `OnboardingTourProps.onBeforeStep` and `HelpMenuProps.onBeforeTourStep` are
  explicitly typed function signatures, not `any`/`Function`.
- **No default exports for components** — `NavDrawer`, all touched files, use
  named exports, matching existing convention.
- **No inline object/array creation in JSX props causing re-renders** — the
  `navItems`/`STATIC_NAV` arrays are module-level constants or `useMemo`-free
  simple derivations already matching how `ArtistLayout`'s existing
  conditional portfolio item was handled before this change (a plain
  `const` computed once per render from stable inputs, not reconstructed
  inside JSX).
- **Role checks stay out of component render logic** — unaffected; no new
  role-gating is introduced, `usePermission`/existing role reads are
  untouched.
- **Tests ship with every change** — §5.
- **Backend rules (tenant isolation, RBAC, structured logging, etc.)** — not
  applicable; no backend file is touched by this prompt.

---

## 9. Final self-check / verification checklist (run before declaring done)

- [ ] `pnpm build`, `pnpm test`, `pnpm lint` all clean.
- [ ] No file outside §4's list was touched — diff reviewed against §3's
      do-not-touch list, specifically confirming no route/role-guard change
      and no backend file touched.
- [ ] Manually resize the browser (or use devtools responsive mode) below
      1024px for each of the four roles and confirm: the horizontal nav
      disappears, the hamburger trigger appears, opening it shows every nav
      item (including the Issuer Feedback badge and the Artist conditional
      "My Portfolio" item when logged in as an artist with a profile),
      clicking an item navigates and closes the drawer.
- [ ] Manually resize above 1024px and confirm the desktop nav is pixel-for-
      pixel the same as before this change (same classes minus the `hidden`
      prefix removed at `lg`+).
- [ ] Manually trigger each of the four onboarding tours (via
      `restartTour()` — check how it's exposed to trigger manually in dev,
      e.g. a dev-only button or Redux devtools action if one exists,
      otherwise reset the `hasCompletedTour` flag via the seeded test
      account data) at a narrow viewport and confirm every nav-targeting
      step now shows its spotlight on the correct drawer item instead of
      being silently skipped — this is the regression §4.5 exists to
      prevent, verify it empirically, not just via the unit test.
- [ ] Resize to 375px on each of `ArtistListPage`, `ClientListPage`,
      `PaymentListPage` (with seeded data present) and confirm the card view
      renders instead of a squeezed/overflowing table, and tapping a card
      navigates correctly; confirm the Edit/Delete buttons on the Artist
      card do not also trigger row navigation.
- [ ] Confirm the empty-state message still shows correctly at 375px on all
      three list pages when a search yields zero results (the `showCards`
      empty-data edge case from §4.3).
- [ ] `docs/claude/conventions.md`'s new "Mobile / Responsive Conventions"
      section is present and matches §6 item 4.
- [ ] For audits/self-review: every checklist row here has a verdict, no
      blanks.

---

## 10. Final deliverable spec

**Code files (new):** `frontend/src/shared/types/navItem.ts`,
`frontend/src/shared/components/NavDrawer.tsx`,
`frontend/src/shared/components/__tests__/NavDrawer.test.tsx`,
`frontend/src/shared/components/__tests__/DataTable.test.tsx` (unless an
existing one is found — extend instead).

**Code files (edited):** `frontend/src/layouts/ClientLayout.tsx`,
`ArtistLayout.tsx`, `OwnerLayout.tsx`, `IssuerLayout.tsx`,
`frontend/src/shared/components/DataTable.tsx`,
`frontend/src/shared/components/OnboardingTour.tsx`,
`frontend/src/features/help/useOnboardingTour.tsx`,
`frontend/src/features/help/components/HelpMenu.tsx`,
`frontend/src/features/artists/components/ArtistListPage.tsx`,
`frontend/src/features/clients/components/ClientListPage.tsx`,
`frontend/src/features/payments/components/PaymentListPage.tsx`, plus each
touched file's existing test file per §5.

**Docs files (edited):** `docs/claude/conventions.md`.

**Docs files (this consultation project's own follow-up, not tonight's
implementing session's job):** after the implementing session finishes, this
consultation project should review the actual diff and add a Decisions Log
entry to `docs/claude/architecture.md` covering: the `NavDrawer`/`NavItem`
shared primitives and that they're now the standard for any new role layout,
the `DataTable.mobileCard` capability and which three pages adopted it, and
the onboarding-tour `onBeforeStep` mechanism and why it exists (so a future
tour step targeting anything else hidden behind an interaction — a dropdown,
an accordion — knows the pattern already exists to reuse).

**Commit message:**
```
feat(mobile): drawer nav for all 4 roles + responsive DataTable cards

- New NavDrawer shared component (Sheet-based, lg:hidden trigger) replaces
  the horizontally-scrolling nav strip on ClientLayout/ArtistLayout/
  OwnerLayout/IssuerLayout; desktop nav (lg+) unchanged
- DataTable gets an optional mobileCard prop: CSS-only dual-render (card
  list sm:hidden, table hidden sm:block) — no new viewport-detection hook
- ArtistListPage/ClientListPage/PaymentListPage migrated to mobileCard
- Fix: onboarding tour steps targeting nav items were silently skipped
  below lg once nav moved into a closed-by-default Sheet (querySelector
  can't find unmounted drawer content) — added OnboardingTour.onBeforeStep,
  threaded through useOnboardingTour/HelpMenu, each layout opens/closes
  the drawer based on the step's target
- docs/claude/conventions.md: new Mobile / Responsive Conventions section
```
