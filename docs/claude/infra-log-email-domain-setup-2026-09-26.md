# Infra log — Company email setup on tattooos.co (2026-09-26)

> **What this file is.** A record of infrastructure work done directly in Google Workspace
> and Cloudflare (not in this repo) so a future Claude Code session has the context if
> follow-up code changes are ever needed. This is a log, not a spec — nothing here
> requires code changes on its own. See the "Follow-up items that do touch this repo"
> section at the bottom for the one thing that does.
>
> Origin: a request from marketing for a dedicated `social@tattooos.co` inbox to register
> the official TattooOS social media accounts (Instagram, Facebook/Meta Business
> Portfolio, TikTok, X, LinkedIn), worked through in the "Pena e Artë - Engineering
> Consultation" project. Full request and requirements:
> `docs/claude/architecture.md` does not contain this doc; the original request lives as a
> Claude Docs page titled "Email setup for TattooOS social accounts — request from
> marketing" (not in this repo — a claude.ai doc, linked from the consultation project).

## Why this was needed

`tattooos.co` had **no MX record** before this work — mail to `support@tattooos.co` (the
address shown to users as the support contact, see `VITE_CONTACT_EMAIL` in `.env` /
`.env.example` / CI/CD workflows / `docker-compose.yml`) was bouncing silently. This was
discovered while scoping the social-accounts request, since every social platform ties
account login, verification codes, and recovery to the email domain used.

## What was done

**Provider:** Google Workspace, Business Starter plan, monthly billing, on the existing
domain `tattooos.co` (no new domain purchased).

**Admin account:** `phi@tattooos.co` — 2-Step Verification on (Authenticator app + phone
number backup), 10 backup codes generated and stored by the owner outside this account.

**DNS records added in Cloudflare** (`tattooos.co` zone), all via the Google Workspace ↔
Cloudflare automated connector (Entri):

| Type | Name | Purpose |
|---|---|---|
| TXT | `tattooos.co` | `google-site-verification=...` — domain ownership proof |
| MX | `tattooos.co` | `smtp.google.com` (priority 10) — mail delivery |
| TXT | `tattooos.co` | `v=spf1 include:_spf.google.com ~all` — SPF |
| TXT | `google._domainkey.tattooos.co` | DKIM signing key |
| TXT | `_dmarc.tattooos.co` | `v=DMARC1; p=none; rua=mailto:phi@tattooos.co` — added manually (Google's flow doesn't add DMARC); `p=none` is monitor-only, intentionally not enforcing yet |

**Existing records left untouched** (Resend, used by the app for transactional/outbound
email — see `docs/claude/architecture.md` for where Resend is used in the codebase):
`send.tattooos.co` (MX + SPF TXT), `resend._domainkey.tattooos.co` (TXT). These live on the
`send.` subdomain and the app's own root-domain SPF, so there is no conflict with the new
Google records above.

**Google Groups created** (Directory → Groups), both owned by `phi@tattooos.co`, both with
**external posting enabled** (required — customers and platforms like Meta/TikTok send
from outside the org, and this is off by default on a new group):
- `support@tattooos.co` — customer support inbox
- `social@tattooos.co` — social platform account logins/notifications

Both are single-member (just the admin) by decision — TattooOS is solo-founder at
this stage, and the owner decided that is sufficient for now rather than deferring pending
a second hire. The marketing request's own requirement ("at least two named people can
access it," "not tied to one person") is accordingly not met today; this is an accepted,
explicit trade-off, not an oversight. Add a second member via Admin console → Directory →
Groups → [group] → Members whenever there's a second person to add; no DNS or code change
needed for that.

**Verified working:** test emails sent from an outside Gmail account to both `support@`
and `social@` arrived in `phi@tattooos.co`'s inbox, not spam.

## Still open (not blocking, tracked outside this repo)

- ~~Password manager~~ — **done (2026-09-26):** **KeePass** stores the `phi@tattooos.co`
  login, the 10 backup codes, and (going forward) social-platform logins registered under
  `social@`.
- Second person on both groups — not planned for now (owner's decision, see above); revisit
  if/when TattooOS has a second person to add.
- A fuller task list covering the domain/website/search-visibility follow-up work (root
  domain has no website, `robots.txt` points at a sitemap that doesn't exist, public
  studio/artist pages have no per-page metadata) was drafted as a separate Claude Docs
  page, **not this repo** — ask the requester for that doc if/when that work is scoped
  into an overnight prompt.

## Follow-up items that DO touch this repo (not done yet, no code changed as part of this log)

- **Meta domain verification**, once marketing requests it: Meta will issue a DNS TXT
  record (handled entirely in Cloudflare, no repo change) *or*, if Meta's flow instead
  offers the meta-tag/file-upload method, that would need a small frontend change
  (a meta tag in `frontend/index.html`, or a static file served at the site root) —
  confirm which method before assuming no-code.
- **Social links in the website footer**, once the social accounts exist and marketing
  sends the final handles — a frontend change, not yet specced.
- Neither item is specced as an overnight prompt yet. No source file in
  `Pena_e_Arte.API/`, `.Application/`, `.Domain/`, `.Infrastructure/`, `.Contracts/`, or
  `frontend/src/**` was touched by the work in this log.
