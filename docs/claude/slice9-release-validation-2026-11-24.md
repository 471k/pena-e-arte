# Slice 9 / E07: validation and release evidence — 2026-11-24

> **Verdict (25 Nov): NOT MET until Desktop rules on it. Every `docs/08` row now has evidence, and none is a
> captured fail.** What is still open:
> 1. **The device and assistive-technology rows are a REPORTED pass, not a captured one** (Phi, 24 Nov 17:10,
>    §1). It has four caveats, carried unchanged: three versions inferred or unconfirmed, versions headed "latest
>    version" rather than read off each device, no build named, and no per-row screen-reader observations.
>    Whether that grade of evidence meets the gate is Desktop's ruling, not this document's.
> 2. **The Google Sign-In residual** (§3b), our own 2.5 s Tab stop in Google's refusal state: fix in
>    #468.
> 3. **The usability pilot**, a post-launch obligation by Phi's decision of 23 Nov, so it is not a launch row.
>
> As first written on 24 Nov (kept): seven of the rows needed a physical device or a screen reader, which no
> agent session can provide. **The four rows a session could run were run on 24-25 Nov (§3b):
> C10 and the four keyboard journeys pass.** 400% reflow and forced-colors focus FAILED on 24-25 Nov, each with a
> named cause. Both were fixed in #464 and **re-run on main at `5901b4aa` on 25 Nov: 400% reflow now passes and
> forced-colors focus passes**, with the before-numbers kept beside them. The switch thumb was fixed in #465 and
> re-run on main at `62c88f4b`, so **forced colors passes for the app's own controls**. Google's sign-in button is
> its own row: the "0x0 Tab stop" first recorded was the button's **failure state on an origin Google refuses**;
> on production it renders and shows a visible focus ring with and without forced colors (§3b, measured 25 Nov).
> **Every §3b probe is Chromium-only.**

- **From:** Claude Code, run 23–24 Nov 2026 (Europe/Paris). **Held for Desktop's review.**
- **What this is:** evidence, not a build. **A row with no evidence is a fail, not a blank** (Desktop's rule). Most
  of the `docs/08` matrix needs real devices and screen readers that this machine does not have, so most rows fail
  for lack of evidence. That is the finding, not an omission. **Since 25 Nov those rows carry Phi's reported
  device pass of 24 Nov (§1), labelled reported, not captured.**
- **Machine:** Windows 10 Pro 10.0.19045. **Browsers driven:** Playwright Chromium 153.0.8010.12; Microsoft Edge
  158.0.4370.33 (installed, driven through Playwright's `msedge` channel). The app ran locally (Vite dev server)
  with the API mocked by the e2e harness (`mockApiFallback`, which answers every unmocked read with an empty list).
  Mocked reads mean empty and error states show where real data would; layout evidence holds for those states only.

## 1. The `docs/08` matrix, run for real

| Environment | What was run | What was seen | Verdict |
|---|---|---|---|
| **Windows Chromium** | **Captured (Code):** §2 200% pass at 8 widths, 4 roles + public pages; §3 keyboard probes; §3b C10, the four journeys, 400% reflow and forced colors; e2e at 390px 100%/200%; axe suite (`accessibility.spec.ts`, axe 4.14 since the-rest run). **Reported (Phi, 24 Nov):** "Windows desktop", Windows 11 26H2 (build 26300.9974) · Chrome 157.0.8103.78/.79 | See §2–§3b; Phi: Pass | **Pass for what was run (captured)**, since #464 for 400% reflow and forced-colors focus (§3b, both Fail before it, re-run on main 25 Nov); the switch thumb fixed in #465 (re-run on main); Google's sign-in button verified on the production origin as its own row (§3b); dialogs and downloads not covered by the capture. **Plus Phi's reported pass (reported, not captured; caveats below)** |
| **Windows Edge** | **Captured (Code):** §3 keyboard probes (S03 record opening, form error focus). **Reported (Phi, 24 Nov):** "Windows desktop", Edge 157.0.4341.60 | Both probes pass, same as Chromium; Phi: Pass | **Captured: Partial** (keyboard probes only; no 200% pass, no downloads). **Reported: Pass (reported, not captured; caveats below)** |
| **Firefox desktop** | **Captured:** none (Playwright's Firefox build is not installed; not downloaded in an autonomous run). **Reported (Phi, 24 Nov):** Firefox 160.0.1 | Phi: Pass | **Pass (reported, not captured; caveats below)** |
| **macOS Safari** | **Captured:** none (needs a Mac). **Reported (Phi, 24 Nov):** macOS 27.0.1 "Golden Gate" · Safari **27.2 (inferred)** | Phi: Pass | **Pass (reported, not captured; the Safari version is marked inferred by Phi; caveats below)** |
| **iPhone Safari** | **Captured:** none (needs an iPhone; the emulated 320/375/390 widths in §2 are not iPhone evidence, since docs/08 says emulation doesn't prove real keyboard, file, calendar or screen-reader behaviour). **Reported (Phi, 24 Nov):** iOS 27.2.1 | Phi: Pass | **Pass (reported, not captured; caveats below)** |
| **Android Chrome** | **Captured:** none (needs an Android phone). **Reported (Phi, 24 Nov):** Android 17 (QPR2) · Chrome 157 (**build not confirmed**) | Phi: Pass | **Pass (reported, not captured; the Chrome build is marked not confirmed by Phi; caveats below)** |
| **Tablet Safari/Chromium** | **Captured (Code):** 768/800/1024/1100 widths in Chromium only (§2). **Reported (Phi, 24 Nov):** "Same OS versions as above" | See §2; Phi: Pass | **Pass (reported, not captured; caveats below)**; layout widths also captured in Chromium |
| **NVDA + Windows browser** | **Captured:** none (NVDA is not installed on this machine). **Reported (Phi, 24 Nov):** NVDA **2026.2 (inferred)** | Phi: Pass | **Pass (reported, not captured; the version is marked inferred by Phi; no per-row observations; caveats below)** |
| **VoiceOver + Safari** | **Captured:** none (needs a Mac or an iPhone). **Reported (Phi, 24 Nov):** iOS 27.2.1 / macOS 27.0.1 | Phi: Pass | **Pass (reported, not captured; no per-row observations; caveats below)** |
| **TalkBack + Chrome** | **Captured:** none (needs an Android phone). **Reported (Phi, 24 Nov):** Android Accessibility Suite 17.0.1.926549743 | Phi: Pass | **Pass (reported, not captured; no per-row observations; caveats below)** |

**Phi's device and assistive-technology pass (24 Nov 17:10 Paris) is a reported pass, not a captured one.** The
source is `claude-shared/handoffs/device-pass-result-2026-11-24.md`, outside the repo. The rows above carry it with
the labels as written there, and no label is upgraded. The four caveats, as recorded there, unchanged:

1. **Three entries are marked by the runner as inferred or unconfirmed** — Safari on macOS, NVDA, and the
   Chrome build on Android. Those are the runner's own annotations and are reproduced unchanged.
2. **The supplied table is headed "Latest version"**, so it records what the current release of each platform
   was rather than a string read from each device at execution. `docs/08` asks for the latter. Recorded as
   supplied.
3. **No build is named.** The pass was reported at 17:10 on 24 November. **`origin/main` moved four times
   that afternoon** — `40d316ad` (#444, support-view write controls), `38e2135d` (#450, the NoSubscription
   refusal), `6d613387` (#451, the S01 zone tail) and **`7e0f95a1` (#452, the money-pages zone fix, merged
   17:01)**. Which of those the pass covers is not recorded.
4. **No per-row observations for the three screen-reader rows.** Those rows carry specific pass conditions —
   whether a failed submit is *announced* and navigable, whether the deposit is read as an **estimate**
   rather than a charge, whether loading is quiet rather than chattering. Which of those were exercised is
   not recorded.

**The Firefox row and the capture.** Before Phi's pass, the Firefox row needed only Playwright's Firefox build
and the others needed a person with the devices. The device-pass file names what would turn the report into a
captured pass: the version strings read off the four devices, and one screenshot of the booking review screen
from the iPhone. That is not asked for, and it is recorded so the option stays visible.

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
| **C10**, body placement without a mouse | **Pass** (25 Nov, §3b) | Tab to "Left Forearm, front" in the booking form's placement list, Space checks it, the chip "Left Forearm · front" appears and the request carries `left_forearm` |
| **The four role journeys end to end** (book, confirm, pay, cancel) | **Pass** (25 Nov, §3b) | All four completed with the keyboard only after sign-in, every Tab stop with a visible focus indicator, light and dark |

## 3b. The four rows a session can run (24-25 Nov)

Run 24 Nov ~23:30 to 25 Nov ~00:30 Paris on branch `feat/the-rest-2026-11-24` (base `e98f433c`), Playwright
Chromium (headless shell 159.0.8154.6), Vite dev server, API mocked per journey. The probes are not committed; they
are kept outside the repo in `code-session-scratch-2026-11-24/the-rest/`. **No row is blank.**

### C10 and the four keyboard journeys: **Pass**

Keyboard only after sign-in. At every Tab stop the probe read the focused element's outline and box-shadow. A
coloured shadow layer with blur or spread, or a real outline, counts; Tailwind's always-present transparent layers
do not. Run in both colour schemes.

| Journey | Tab stops | Stops with no visible focus | What was done with the keyboard |
|---|---|---|---|
| **C10 + J1** client books | 79 | 0 (see note) | Artist picker (Radix Select: Enter, ArrowDown, Enter); date and time typed into the field's segments; session length (Enter, arrows to "2 hours", Enter); description; **placement: Space on "Left Forearm, front", chip shown, sent as `left_forearm`**; Enter on "Request Appointment" |
| **J2** client pays the deposit | 4 | 0 | "Cash", then "Confirm — I'll pay cash at the studio"; the confirmation shows and the cash declaration is sent. The card path hands off to POK's hosted page, which is not ours and was not driven |
| **J3** owner confirms a request | 28 | 0 | From the top of the appointment page to "Confirm appointment", Enter; the confirm request is sent |
| **J4** client cancels | 21 | 0 | "Need to change or cancel?", then "Cancel appointment"; **focus opens on "Keep appointment"** (the safe choice); Shift+Tab to "Yes, cancel", Enter; the cancellation is sent |

Note: the probe's instant reading flagged the Date & Time field (`datetime-local`) once per run. Read again once it
settles, the same field carries the 4px ring (`rgb(100, 48, 168) 0 0 0 4px`), and a screenshot shows the ring and
the browser's highlighted day segment. Counted as visible, and recorded so nobody re-finds it.

### 400% reflow (WCAG 1.4.10), 1280x1024: **Pass on main since #464** (was **Fail**, 24-25 Nov)

**Coverage: Chromium only** (Playwright's headless shell). Firefox and WebKit were not run, so this verdict says
nothing about them.

**After #464, re-run on main at `5901b4aa` (25 Nov ~04:50 Paris), the same probe, fixtures and stops:**

| Page | Stops | Before (24-25 Nov, main without #464) | After (main `5901b4aa`) |
|---|---|---|---|
| `/discover` | 59 | **20 entirely hidden** under the 234px sticky header | **0 hidden, 0 partly covered** |
| `/artist/{slug}` | 57 | **3 entirely hidden** (the studio link, "Powered by TattooOS") | **0 hidden, 0 partly covered** |
| `/s/{slug}` | 57 | **1 entirely hidden** (the in-page "Book an Appointment" link) | **0 hidden, 0 partly covered** |
| `/schedule`, owner, 14 appointments | 89 | **27 entirely hidden and 38 partly covered**, among them Confirm, Decline, Reschedule, Cancel, Mark no-show and Complete on the session cards, the artist filter and the Dashboard link | **0 hidden, 0 partly covered** |

What changed: measured `scroll-padding` for pinned chrome (W3C C43), with a floor at the signed-in shell header's
height so a failed measurement cannot silently give 0. The three headers that wrap tall (Discover, the public
studio/artist header, Schedule) also unpin on a **short** viewport (max-height 480px). The quick-book bar stays
pinned. The free band on the studio and artist pages is 187px against 138px portfolio tiles. Sideways scroll stays
0px. The findings below are the **before** state and are kept as the record.

400% of 1280x1024 is a 320x256 CSS px viewport at 100% text. 26 pages: the public and auth pages and every role's
pages from §2, with the public studio and artist pages on real fixtures. Under the empty fallback mock those two
crash, which first read as 814px of sideways scroll: a probe artefact, not a finding.

- **Two-dimensional scrolling: none.** Sideways scroll is **0px on all 26 pages**. The artist page's tabs and
  Discover's filter chips scroll inside their own strips, which is a component scroll, not the page's.
- **Navigation is reachable** on every signed-in page ("Open navigation menu").
- **Fail 1, WCAG 2.4.11 Focus Not Obscured (AA in 2.2), public pages.** With the page scrolled, Tab put focus on
  controls that were **entirely hidden behind sticky or fixed chrome**. On `/discover`: "All", "Fresh Tattoos",
  "Healed Tattoos" and the style filters, under a sticky header **234 of the 256px** tall (measured: "All" focused
  at y 110-146, the header covering 0-234). On `/s/{slug}`: the in-page "Book an Appointment" link. On
  `/artist/{slug}`: the studio link and "Powered by TattooOS". The last two are under the 73px header and the 69px
  quick-book bar. Usual fix: `scroll-padding-top` and `scroll-padding-bottom` equal to the pinned chrome, and a
  Discover header that is not sticky, or much shorter, below the 420 breakpoint.
- **Fail 2, function, `/discover`:** with 234 of 256px pinned, the results are read through a 22px slit.
- **Fail 3, WCAG 2.4.11, the signed-in Schedule (retested 25 Nov with real fixtures).** This row first read "watch,
  not a fail", because under the empty mock nothing focusable sat below the header. That was the same empty-mock
  condition that produced the phantom 814px, and it should have read **not run**. Retested at 320x256 with 14
  appointments, tabbing 45 stops forward and back: of **89 stops, 27 were entirely hidden and 38 partly covered**
  behind the 56px app header and the Schedule's own header, which wraps to 137px. The hidden ones include Confirm,
  Decline, Reschedule, Cancel, Mark no-show and Complete on the session cards, the artist filter and the Dashboard
  link. Same probe, same day, on main: `/discover` 20 hidden of 59, `/artist/{slug}` 3 of 57, `/s/{slug}` 1 of 57.
- **Fixed by #464, merged 25 Nov as `5901b4aa`,** and re-run on main (the table above): 0 of 262 stops hidden or
  partly covered. The verdict changed only after the re-run on main, not when the branch passed.
- **The cookie banner** covers 137 of 256px until it is dismissed, and "Got it" is 8 to 14 Tab presses from the top
  (public 14, owner 12, client 9, artist and admin 8). It can be dismissed, so it is not a reflow fail; recorded
  because at 400% it hides half the screen until then.

### Forced colors, the app's own controls: **Pass** on main since #464 + #465 (was **Fail**, 24-25 Nov: 4 of 11 sign-in, 10 of 25 booking, 7 of 25 dashboard and 7 of 25 schedule stops with no indicator; now 0 of 86). Chromium only.

This row covers what the app draws. Google's sign-in button is a separate row below, because conformance is a
property of the page as delivered, third-party content included, so a Pass here does not say anything about it.

**Coverage: Chromium only**, with `forced-colors: active` emulated. That is a mild limit for this row, because
Windows contrast themes are used overwhelmingly through Edge and Chrome, both Chromium. It still says nothing about
Firefox.

**After #464, re-run on main at `5901b4aa` (25 Nov ~04:45 Paris):** focus-indicator probe, which now also
requires the outline colour not to be transparent:

| Page | Before (24 Nov) | After (main `5901b4aa`) |
|---|---|---|
| Sign-in | **4 of 11** stops with no visible indicator | **0 of 11** |
| Client booking | **10 of 25** (incl. "Skip to content", the artist picker, the switch) | **0 of 25** |
| Owner dashboard | **7 of 25** | **0 of 25** |
| Owner schedule | **7 of 25** | **0 of 25** |

Outlines compute to the forced system colour. "Skip to content" is outlined (screenshot). Filled buttons keep their
shape (`forced-colors:border`).

**The switch thumb, fixed by #465 and re-run on main at `62c88f4b` (25 Nov ~05:20 Paris).** Before: the thumb
vanished (a Canvas disc on a Canvas track, its shadow removed). The track's outline survived, so off read as an
empty pill and on only by its check. Desktop ruled this **not accepted**. After: off = a CanvasText thumb in the
outlined track, on = a Highlight track with a HighlightText thumb and check. The committed
`e2e/toggle-switch-forced-colors.spec.ts` passes on main, and screenshots of both states are kept outside the repo
(`code-session-scratch-2026-11-24/rulings-2026-11-25/switch/`). The focus probe on main still reads 0 of 25 on
the booking page.

**Verdict: the app's own controls pass in forced colors** (0 of 86 stops with no indicator across the four pages,
and the switch readable in both states).

The findings further below are the **before** state and are kept as the record.

### Focus visible, third-party Google Sign-In: **Pass**, verified **on production** (`app.tattooos.co`), **Chromium only**. One known defect in our own code: a Tab stop of up to 2.5 s in Google's refusal state, fixed in #468. Was recorded **Open** on 25 Nov; Desktop withdrew that ruling the same day, because it rested on a probe artefact.

**Verdict basis, in the row:** on production the button renders at **418x44**, and its focus ring is **visible with
and without `forced-colors: active`**, checked by **comparing pixels** of the button's box unfocused against
focused in both modes. There is no conformance failure in normal operation. **Chromium only**: Firefox and WebKit
were not probed.

**What was first recorded (kept as the record):** in the `62c88f4b` forced-colors re-run, Google's script had
loaded on `/login` and its iframe was **0x0, took a Tab stop, and had no visible indicator** (12 stops, 1 without,
and that one was the iframe). In the `5901b4aa` run the script had not loaded: 11 stops, 0 without. Desktop ruled
the row Open, neither passed nor dropped, pending a look at the render options.

**What the look found (25 Nov ~13:30 Paris, Playwright Chromium, probes outside the repo in
`code-session-scratch-2026-11-24/rulings-2026-11-25/gsi/`):**

- **The 0x0 iframe is the button itself, not an extra element.** It is `accounts.google.com/gsi/button`, titled
  with the button's label. On the local probe origin Google logs *"The given origin is not allowed for the given
  client ID"* and never sizes it. All the §3b probes ran on a local origin, so they all saw the failure state.
- **No configuration changes that.** Rendered fresh on the local origin, all ten variants stayed 0x0 with
  `tabIndex` 0: the current options (`standard`, `filled_black`, `large`, `continue_with`, `rectangular`, width
  300); `type: "icon"`; `theme: "outline"` + `size: "medium"`; `shape: "pill"` with no width; `itp_support: true`;
  `use_fedcm_for_button: true`; `use_fedcm_for_prompt: false`; `ux_mode: "redirect"`; `ux_mode: "popup"`;
  `locale: "en"`. The size depends on the origin being allowed, not on a render option.
- **On production (`app.tattooos.co/login`, read-only: loaded and tabbed, never clicked) the button renders 418x44
  within 300 ms and is an ordinary Tab stop**, between "Sign in" and "Sign up".
- **Its focus is visible in both modes.** Pixel comparison of the button's box, unfocused against focused: they
  differ in both, and the screenshots show a 2px ring around the button when focused, with and without
  `forced-colors: active`. The ring is **Google's, drawn inside their frame**: the outer `<iframe>` computes
  `outline: none` and does not match `:focus-visible`, because focus moves into Google's document. So the
  guarantee rests on Google's styling, which we cannot test from outside beyond this.
- **Side observation, not a fail:** in forced colors the unfocused button has no boundary at all. Its text still
  identifies it as a control.

**Known defect, ours (the residual):** when Google refuses to render the button (wrong origin, or their service
failing), the 0x0 iframe is a Tab stop for up to **2.5 s**, until `GoogleSignInButton`'s render check marks it
failed and hides the container (`display: none`, which removes the stop; confirmed: the Tab walk at +5 s on the
local origin has no iframe stop). It concerns only our DOM, not Google's. **Follow-up: #468** (Desktop
approved the fix on 25 Nov: its own PR, merge on green). It keeps our container out of the Tab order until
Google has rendered into it, proved by a probe that counts Tab stops with Google's script refused.

**`tabindex="-1"` on Google's iframe was not tried and is not proposed.** Nothing found here needs it.

Chromium with `forced-colors: active` emulated (confirmed with `matchMedia`). Sign-in, the client booking page, and
the owner dashboard and schedule.

- **Fail: focus is not visible on the shared primitives.** `Button`, `Input` and `ToggleSwitch` draw focus as
  `focus-visible:outline-none` plus a `ring`, which is a box-shadow, and forced-colors mode removes box-shadows.
  Measured: no outline and a computed `box-shadow: none` on **4 of 11** stops on sign-in (the email field, "Show
  password", "Sign in", the logo link), **10 of 25** on the booking page (including "Skip to content", the artist
  picker and the switch), and **7 of 25** on the dashboard and on the schedule. Screenshots of "Sign in" focused and
  not focused are identical. Elements with a native or bordered indicator (links, the user menu) pass. Usual fix,
  one pattern in the primitives: `focus-visible:outline-hidden` instead of `outline-none` (a transparent outline,
  which forced-colors mode draws), and the same for the select trigger and any link that sets `outline-none`.
- **Disappears:** filled buttons lose their shape (the background goes and there is no border), so "Sign in" reads
  as plain text; the switch's thumb vanishes.
- **Colour alone: none found on the surfaces seen.** The switch's on state still shows its check icon (off is an
  empty pill), and statuses are text. Charts and calendar events were empty under the mock, so they were not seen.

## 4. The audit's unverified rows (`e01-e03-coverage-audit-2026-11-15.md` §7)

| Row | Was | Verdict now | Evidence |
|---|---|---|---|
| **S03** focus behaviour (opening a record from the keyboard) | unverified | **Present** | §3: 29 Tab stops, all with visible focus, Enter opens the record; Chromium and Edge |
| **C10** focus behaviour (body placement without a mouse) | unverified | **Present** | §3b: the placement list is reached, checked and confirmed from the keyboard (25 Nov) |
| **S05** at phone width on the two uncovered surfaces | unverified | **Partial** | §2 at 390/420: the header passes for every role; page content fails on Payments, Schedule, Dashboard and Messages |
| **S06** announcements | unverified | **Unverified → fail (no evidence)** | Needs a screen reader; none on this machine (§1). Phi's reported screen-reader pass (§1) records no per-row observations, so it does not say whether announcements were exercised, and this row is not changed by it |
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

**Update 24 Nov: the audit is done, and S01's verdict is Partial** (`docs/claude/s01-zone-audit-2026-11-24.md`,
PR #451). The verdict is written with the audit, not when the fixes land. Of the 61 non-test files with plain
`toLocale*` calls: 30 are viewer-zone and correct, 14 are safe for another reason, 8 have a studio-zone defect,
6 have a date-only defect, 2 are undecided and 1 is not a date. A third class turned up: **date-only values**
(date of birth, closure days, tattoo record dates), which are stored as UTC midnight and so read as the day before
west of UTC.

- Fixed by #451: ArtistDetailPage bookings, ClientDetailPage appointments (`:620`), and all the date-only sites.
- Fixed by #452 (the money pages): PaymentDetailPage, PaymentListPage, UnsettledPaymentsSection, MyEarningsPage
  and NoShowConfirmDialog.
- The ruling MyEarnings needed is made: "Paid" is studio-zone, because money placed in a studio period counts as
  studio-day. The rule is written above `formatStudioTime`.
- Still open: the review and conduct-report visit pickers (their responses carry no zone, so they need a backend
  field), and booth rent's "Charged" date (undecided). **S01 becomes Present when those close.**
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
