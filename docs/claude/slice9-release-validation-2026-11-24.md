# Slice 9 / E07: validation and release evidence — 2026-11-24

- **From:** Claude Code, run 23–24 Nov 2026 (Europe/Paris). **Held for Desktop's review.**
- **What this is:** evidence, not a build. **A row with no evidence is a fail, not a blank** (Desktop's rule). Most
  of the `docs/08` matrix needs real devices and screen readers that this machine does not have, so most rows fail
  for lack of evidence. That is the finding, not an omission.
- **Machine:** Windows 10 Pro 10.0.19045. **Browsers driven:** Playwright Chromium 153.0.8010.12; Microsoft Edge
  158.0.4370.33 (installed, driven through Playwright's `msedge` channel). The app ran locally (Vite dev server)
  with the API mocked by the e2e harness (`mockApiFallback`, which answers every unmocked read with an empty list).
  Mocked reads mean empty and error states show where real data would; layout evidence holds for those states only.

## 1. The `docs/08` matrix, run for real

| Environment | What was run | What was seen | Verdict |
|---|---|---|---|
| **Windows Chromium** | §2 200% pass at 8 widths, 4 roles + public pages; §3 keyboard probes; e2e at 390px 100%/200%; axe suite (`accessibility.spec.ts`) | See §2–§3 | **Pass for what was run** (listed per row in §2/§3); dialogs, downloads and 400% reflow at 1280 covered only by the 320px column (1280 at 400% = 320 CSS px) |
| **Windows Edge** | §3 keyboard probes (S03 record opening, form error focus) | Both pass, same as Chromium | **Partial**: keyboard probes only; no 200% pass, no downloads |
| **Firefox desktop** | Not run: Firefox 160.0.1 is installed but Playwright's Firefox build isn't, and installing it is a new download not taken in an autonomous run | — | **Fail (no evidence)** |
| **macOS Safari** | Not run: no Mac | — | **Fail (no evidence)** |
| **iPhone Safari** | Not run: no device. Emulated widths 320/375/390 in Chromium are in §2 and are not iPhone evidence (docs/08: emulation doesn't prove real keyboard, file, calendar or screen-reader behaviour) | — | **Fail (no evidence)** |
| **Android Chrome** | Not run: no device | — | **Fail (no evidence)** |
| **Tablet Safari/Chromium** | 768/800/1024/1100 widths in Chromium only (§2) | See §2 | **Fail (no device evidence)**; layout widths covered in Chromium |
| **NVDA + Windows browser** | Not run: NVDA isn't installed | — | **Fail (no evidence)** |
| **VoiceOver + Safari** | Not run: no Mac or iPhone | — | **Fail (no evidence)** |
| **TalkBack + Chrome** | Not run: no Android device | — | **Fail (no evidence)** |

**What closes the fails:** one session on a real iPhone and an Android phone (the usability pilot tasks in docs/08
are the natural script), NVDA on this machine (free; installing it is Phi's call), and a Mac with VoiceOver. The
Firefox row needs only Playwright's Firefox build.

## 2. Rendered pass at 200% text

Real Chromium, the root font at 200% before the app loads (as a browser text-size setting is), slice 6's widths,
the four role layouts and the public pages, after Phase 2 (#441) **and its follow-up #445** (built in this run
because this pass found the first one incomplete). The number is the page's sideways scroll in CSS px
(`scrollWidth − clientWidth`); **0 is the bar**. Run: 168 measured; 32 overflow; 16 no header.

| role | page | 320 | 390 | 420 | 768 | 800 | 1024 | 1100 | 1440 |
|---|---|---|---|---|---|---|---|---|---|
| owner | `/dashboard` | **100** | **30** | **9** | 0 | 0 | 0 | 0 | 0 |
| public | `/artist/maria-silva` | **253** | **324** | 0 | 0 | 0 | 0 | 0 | 0 |
| public | `/s/tinta-alma` | **253** | **324** | **294** | 0 | 0 | 0 | 0 | 0 |
| owner | `/schedule` | **156** | no header | **56** | 0 | 0 | 0 | 0 | 0 |
| public | `/discover` | **205** | **276** | **246** | 0 | 0 | 0 | 0 | 0 |
| owner | `/clients` | **48** | no header | 0 | 0 | 0 | 0 | 0 | 0 |
| public | `/feed` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| owner | `/artists` | **48** | no header | 0 | 0 | 0 | 0 | no header | 0 |
| owner | `/payments` | **205** | no header | **105** | 0 | 0 | **13** | 0 | 0 |
| owner | `/studios/me` | **48** | no header | 0 | 0 | 0 | 0 | 0 | 0 |
| owner | `/artists/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb` | **48** | no header | 0 | 0 | 0 | 0 | 0 | 0 |
| artist | `/today` | **48** | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| artist | `/schedule` | no header | 0 | 0 | 0 | 0 | no header | 0 | 0 |
| artist | `/clients` | **48** | 0 | 0 | 0 | 0 | no header | 0 | 0 |
| artist | `/artists/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb` | no header | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| client | `/my-appointments` | **48** | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| client | `/book` | **69** | 0 | 0 | no header | 0 | 0 | no header | 0 |
| client | `/waitlist/mine` | **48** | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| client | `/messages` | no header | **108** | **78** | no header | **8** | **296** | **220** | 0 |
| admin | `/platform` | **48** | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| admin | `/platform/studios` | **48** | 0 | 0 | 0 | 0 | no header | 0 | 0 |

**Before #445, on main with #441 only** (same pass, 23 Nov ~22:00 Paris): the owner's header overflowed by **446px
at 768, 414 at 800, 214 at 1024, 138 at 1100**, and the artist's by 92 at 768 and 60 at 800. #441's fold had been
calibrated on the phone layout only; #445 also folds when the header's content overflows. Those columns are now 0.

**What still fails, each a finding, none fixed here (this slice ships a document):**

- **320px at 200% text, every role (about 48px, more on some pages):** the folded header still needs more than 320
  CSS px. This is past WCAG's own tests (1.4.10 is 320px at 100%; 1.4.4 is 200% text at ordinary widths), but it
  misses the zero-scroll bar, so it is recorded as a fail.
- **Public pages' header nav, 205–324px at 320–420:** `PublicPageHeader`'s nav doesn't wrap or fold at 200% text.
  The same fold idea would apply; it is outside Phase 2's scope.
- **Owner Payments toolbar, 105px at 420 and 13 at 1024:** a `flex items-center gap-3` row of buttons that doesn't
  wrap.
- **Owner Schedule, 56px at 420:** the artist select is `max-w-[14rem]`, which is 448px at 200%.
- **Owner Dashboard, 30px at 390 and 9 at 420:** a card row with a chevron button.
- **Client Messages, 108/78px at 390/420 and 296/220px at 1024/1100:** the conversation list's empty-state aside
  (`w-80` at `page-md`) at 200% text.
- **"no header" cells (16):** the page didn't render its header within 8 s under the mocked API on this laptop, so
  those cells are **unmeasured, which counts as a fail**, not as a pass.

Caveat: the API is mocked with empty answers, so lists, tables and long names that real data would bring are not
in these pages. A real-data pass would find more, not less.

## 3. Keyboard-only walk

Real Chromium and Edge, keyboard only after sign-in.

| Check | Result | Evidence |
|---|---|---|
| **S03**, a record opens from the keyboard (owner, Clients) | **Pass** in Chromium and Edge | 29 Tab stops from the page top to "João Silva"; **every stop shows a visible focus ring**; Enter opens `/clients/client-0001` |
| **Forms**: submitting `/clients/new` empty moves focus to the first problem (the audit's open react-hook-form question) | **Pass** in Chromium and Edge | Focus lands on the first invalid input, with a visible ring, not on the body |
| **No skip link** | **Finding** | The first 23 Tab stops are the header and the sidebar on every staff page (trail recorded: collapse, Owner/Artist, feedback, help, messages, bell, account, then 16 nav entries). WCAG 2.4.1 (A) can be met by landmarks, which a screen reader can jump between, but a keyboard user without one cannot. A "Skip to content" link is the usual fix: one component in the four layouts. Not built (this slice ships a document) |
| **C10**, body placement without a mouse | **Not run → fail (no evidence)** | Needs a client, a profile and the body map's edit mode mocked end to end; not done tonight |
| **The four role journeys end to end** (book, confirm, pay, cancel) | **Not run → fail (no evidence)** | Each needs a chain of mocked writes and state; the checks above cover their entry points (records open, forms report errors to focus) but not the journeys |

## 4. The audit's unverified rows (`e01-e03-coverage-audit-2026-11-15.md` §7)

| Row | Was | Verdict now | Evidence |
|---|---|---|---|
| **S03** focus behaviour (opening a record from the keyboard) | unverified | **Present** | §3: 29 Tab stops, all with visible focus, Enter opens the record; Chromium and Edge |
| **C10** focus behaviour (body placement without a mouse) | unverified | **Unverified → fail (no evidence)** | Not run (§3) |
| **S05** at phone width on the two uncovered surfaces | unverified | **Partial** | §2 at 390/420: the header passes for every role; page content fails on Payments, Schedule, Dashboard and Messages |
| **S06** announcements | unverified | **Unverified → fail (no evidence)** | Needs a screen reader; none on this machine (§1) |
| **AP12** | unverified | **Present**, per the audit's 22 Nov addendum (slice 6) | Not re-run in this pass; the addendum's own evidence stands |
| **S01 group B** (eight calendar dates in a negative-offset zone) | unverified | **Present for every value with a test** | The nightly sweep's UTC-11 leg runs the whole frontend suite in Pacific/Pago_Pago; after #440 it fails nothing. A value with no test is not covered |
| **react-hook-form** focus on the first invalid field | unverified | **Present** | §3: an empty `/clients/new` submit moves focus to the first invalid input, Chromium and Edge |

## 5. S01 re-verdicted: the model exists, and it was not applied everywhere

Slice 3 marked S01 ("one explicit time-zone model, applied") **Present**. The nightly frontend clock sweep's UTC+14 and
UTC-11 legs (#439, first run 23 Nov) showed that verdict was wrong: 13 tests in 7 files failed, and two of the three
classes were live defects. **S01 was Partial from 23 Nov until #440.**

What #440 changed (merged `38a4de76`, live 23 Nov 19:33 UTC):

- **The waitlist had no zone at all.** `MyWaitlistPage` showed an offered slot's day in the viewer's zone, and an offer
  expires. The entry now carries `StudioTimezone`, and the page and the staff queue use it. A missing zone reads
  "Time unavailable", never a guess.
- **First paints.** Each `withStudioTimeZone` caller was audited with one question: does it paint a time before the
  zone has loaded? Six did, and were fixed to show a pending mark (`…`) instead: ClientAppointmentDetailPage,
  AppointmentDetailPage, ClientCommunicationCard, OwnerExceptions, DesignDetailPage, TimeOffImpactDialog. Four don't,
  with the reason recorded: AppointmentCard (parent-gated), TodayPage/TodaySessionCard, WaitlistClaimPage, appointmentLabel.
- **Record timestamps** (paid at, saved, generated, requested) stay in the viewer's zone by rule. Their tests compute the
  expectation with the same `Intl` call, because no fixture time keeps one date across a 25-hour span.

**What is still open, so S01 is "Present except as listed", not unconditionally Present:**

- Three callers on money pages paint early and were deliberately left out of #440: PaymentDetailPage's appointment
  date, MyEarningsPage, NoShowConfirmDialog. They are not fixed yet (see the run report). MyEarningsPage's "Paid" date
  also follows the studio's zone, which contradicts the record-timestamp rule; that needs a ruling, not just a fix.
- `ClientDetailPage.tsx:619` formats an appointment date with the plain viewer-zone formatter. It is outside the
  `withStudioTimeZone` list, as the waitlist was, so a full audit of plain `toLocale*` calls on appointment-type
  instants is the remaining S01 step.
- The audit's S01 group B (eight calendar-date values, "needs one check in a negative-offset zone"): the -11 sweep leg
  now runs the whole frontend suite in Pacific/Pago_Pago nightly. After #440 it fails nothing, so every group-B value
  with a test is covered. A value with no test is not, so this is evidence, not proof.

## 6. The five partials

| Item | Blocker | Owner | State |
|---|---|---|---|
| **S04** durable drafts + **C05** resume | One decision: whether drafts holding health answers and photographs may be stored at all, for how long, and how erasure reaches them (the fifth storage location in the Legal ask) | **Legal, then Phi** (asked 23 Nov, one combined ask) | Nothing built, in any form, until Legal answers |
| **D04** kept input and receipts | Five unaudited mutations now write audit entries (#435). Kept-input-plus-receipt for the other 19 admin mutations is **backlog by Phi's decision** | Phi (decided) | Partial by decision |
| **D05** support-view write controls | Phase 4b built as PR (this run): write controls disabled with the support-session reason | **Desktop** (review, held) | Partial until merged |
| **D08** studio list paging | Deferred. Trigger: the list endpoint's p95 over one second (alert `admin-list-p95-slow`, live since #436). **See §7: the trigger's dropped half was dropped on a false premise** | Phi / Desktop | Deferred with a measured trigger |

## 7. The `TotalStudios` "discrepancy" does not exist (my error, 23 Nov)

My 23 Nov note (`code-note-d04-named-and-d08-watch-2026-11-23.md` §2) said `TotalStudios` excludes suspended studios
while the list returns them. **It doesn't.** `GetPlatformStatsQuery` loads every studio
(`db.Studios.IgnoreQueryFilters()`, no `Where`) and sets `totalStudios = studios.Count` (`:42`). Suspended studios are
counted in it, and also, separately, in `suspendedStudios` (`:37`). `GetStudiosQuery` (the admin list) reads
`db.Studios` with no filter, and `Studio` has no global query filter, so the list and the dashboard count the same
rows.

**Consequence:** Desktop dropped the "~300 studios" half of D08's paging trigger because of my claim. That half is
readable on the dashboard after all, so dropping it was not necessary. Whether to restore it is Desktop's call. Worth
noting for that call: `GetStudiosQuery` runs `StudioCurrencyLock.IsLockedAsync` per row (four EXISTS queries each), so
the list's cost grows by rows times queries. The p95 alert will see that, and a studio count would anticipate it.

## 8. Benchmark and the target-size trap

WCAG 2.2 AA is the standard this document measures against. **AA's target-size minimum (2.5.8) is 24×24 CSS px, with
spacing exceptions. The project's 44px comfortable target and 46px control height are this project's own goals, not
WCAG.** No row above cites 44 or 46px as a WCAG requirement. The reflow rows use 1.4.10 (320 CSS px at 100%) and
1.4.4 (200% text) by name, and the 320px-at-200% failures are recorded against Desktop's zero-scroll bar, which is
stricter than either.

## Help sync

None: this slice ships a document. No user-visible surface changes.
