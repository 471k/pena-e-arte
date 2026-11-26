# Guest booking drafts: the open question (spec only, not built) — 2026-11-24

- **From:** Claude Code, Phase B of Desktop's five-answers instruction (24 Nov). That instruction says to build the
  signed-in client's draft, then **stop before the guest draft**: write it up with the options, and don't pick one.
- **Built for signed-in clients:** a server-side draft (`BookingDraft` + `BookingDraftAttachment`), owned by the login at
  one studio, kept for seven days from the last save, deleted on send, on "Start over" and on erasure. It never holds
  the safety notes (`safetyNotes`). The server refuses a payload that carries the key at all.

## Why the guest case is different

A guest has **no account**, so a guest draft has no owner to attach to. Whatever stands in for an owner (the browser,
an email address, a link) is a different privacy posture from a draft behind a sign-in, and it may reopen the Legal
question that Phi closed for signed-in drafts.

What a guest form holds, without the safety notes: the studio, service, artist, date and time, description, style,
placement, how they heard about the studio, notes, codes, **their name, email and phone**, and photos already uploaded
under `appointments/guest-pending/`. `GuestPendingUploadCleanupJob` deletes those photos after **48 hours** if no
booking references them. Any guest draft that outlives 48 hours must either be counted by that job, or accept that its
photos are gone.

## The options (no decision taken)

| | Option | Where it lives | Privacy posture | Cost | Open questions |
|---|---|---|---|---|---|
| A | **Local only** | The guest's own browser (localStorage), on this device | Contact details and photo links sit in browser storage, readable by anyone at that device. S04 says to keep "sensitive health answers and body photos out of general browser storage". Photo links count | Small: no server change | Do body-photo links count as body photos for S04? How long does it live, given the browser doesn't expire it on its own? |
| B | **Emailed resume link** | Server, like the signed-in draft, owned by a one-time signed token sent to the email the guest typed | The same store as signed-in drafts, but the owner is whoever holds the email. Typing someone else's address sends them the link | Medium: a token (the pattern of the waitlist claim, C09), an email, and a public endpoint (a new AllowAnonymous row) | Is an emailed link "consent to store"? The 48-hour photo job would need to count these drafts. Rate limits on sending |
| C | **None** | Nothing is kept | The status quo. The exit guard already warns before leaving, so the recovery path is the warning | None | None. S04's "resumable" half stays Partial for guests |

## What should decide it

1. **Legal:** whether A's browser storage, or B's emailed link, is acceptable for a guest's contact details and photo
   links, beside the signed-in decision.
2. **Phi:** whether resuming a guest booking is worth a new public endpoint (B), or whether C, with sign-up offered
   on return, is enough.
3. **Either way:** C05 (resuming after the account is created) depends on this answer. A guest draft that survives
   account creation is option B with an ownership transfer on top.

Until then, guests get option C, which is what ships today.
