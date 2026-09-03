# Overnight Prompt — Operational Hardening (Tier 4)

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt. **Mixed mode**: §1 (ACME email) and §2 (GeoIP path) are
> fully autonomous, no dependency on anything else. §3 (alerting), §4 (status page), §5 (uptime
> monitoring) all need a live Grafana instance and a reachable public URL — **gated on
> `docs/claude/overnight-prompt-production-deploy-first-2026-09-03.md` having landed**; check
> `kubectl get pods -n monitoring` before starting those sections and stop if Grafana isn't
> `Running`. §6 (backup/DR) and §7 (secrets rotation drill) are partly BLOCKING-MANUAL (real
> external accounts, a real restore test) — do what's autonomous, hand off the rest explicitly.

**Date logged:** 2026-09-03
**Requested by:** Phi
**Origin:** Engineering-consultation gap audit. None of these seven items are oversights — the
K3s and CD prompts both explicitly named alerting, a status page, and DB backup/restore as
out-of-scope follow-ups. This prompt is that follow-up.

**Checkpoint before starting:**
```bash
git status
git checkout main && git pull
git checkout -b chore/operational-hardening
git commit --allow-empty -m "checkpoint: before operational hardening work"
```

---

## 1. ACME contact email — real ops inbox (autonomous, but check with Phi on the domain first)

`k8s/base/cluster-issuer.yaml`'s `email` field is `phisoftwaresolutions@gmail.com` — the file's
own comment already documents this as temporary, confirmed by Phi 2026-09-03, "swap for a
dedicated ops inbox (e.g. `ops@tattooos.co`) once one exists." **Before changing this, confirm
with Phi that an `ops@tattooos.co` (or equivalent) inbox actually exists and is monitored** —
this session should not invent the swap without confirming the destination inbox is real, since
Let's Encrypt renewal-failure emails going to a dead address is worse than the current state.
If Phi confirms an inbox: update the `email` field, note in the same comment block that changing
this does not require reissuing existing certs (already documented, keep that line), and commit
with a message explaining the swap. If no such inbox exists yet, leave this as-is and say so in
the final summary — this is explicitly low-priority per the source audit ("doesn't block
issuance, purely a correctness fix").

---

## 2. GeoIP data path (autonomous)

`k8s/base/api-configmap.yaml`'s `GeoIp:*` keys are deliberately left unset — no K8s volume/PVC +
population mechanism exists for the GeoLite2 `.mmdb` files that `docker-compose.yml` mounts
locally in dev. Confirm this is still the state (`grep -n GeoIp k8s/base/api-configmap.yaml`)
before doing anything. This is named as low-priority, gracefully-degraded-until-fixed in the
source audit — **do not build the full mechanism in this prompt** (a real K8s solution needs a
recurring download job for MaxMind's GeoLite2 updates, MaxMind license-key management, and a
PVC or init-container population strategy — real design work, not a quick fix). Instead:
1. Confirm the app actually degrades gracefully with `GeoIp:*` unset (check whatever service
   consumes it — grep for `GeoIp` in `Pena_e_Arte.Infrastructure`/`.Application` — and confirm
   it null-checks rather than throwing at startup).
2. Write up the real mechanism as a short design note appended to `docs/claude/architecture.md`
   (a new subsection under wherever GeoIP-related features are already documented, or a new one
   if none exists) — options: (a) a small init-container that downloads the `.mmdb` on pod
   start from a MaxMind license URL stored as a K8s secret, writing to an `emptyDir` shared with
   the main container; (b) a recurring Hangfire/CronJob that re-downloads into an R2-backed
   volume. Name a recommendation, not just a list, and say why the app doesn't need this to
   ship — do not implement either option in this prompt.

---

## 3. Alerting / on-call routing (gated on production being live)

```bash
kubectl get pods -n monitoring     # Grafana must be Running before proceeding
```
Nothing pages anyone today — confirmed no `AlertRule`/Alertmanager config anywhere in `k8s/`.
Build:
1. Grafana Alerting rules (provisioned via `k8s/observability/grafana-configmap.yaml`'s
   provisioning mechanism — check whether it already supports a `provisioning/alerting/`
   directory pattern, matching however its existing dashboards/datasources are provisioned,
   before inventing a new mechanism) for at minimum: API pod not `Ready` for >5 minutes, error
   rate spike (5xx rate from the existing Loki/Tempo instrumentation), and Hangfire job failure
   rate. Pick concrete thresholds and say why in a comment — don't leave them as arbitrary
   defaults with no rationale.
2. A real receiver. Ask Phi (via the final summary, not by blocking mid-session) which channel
   to use if none is obvious from existing project context — email via Resend (already
   configured for app notifications) is the lowest-setup option and should be the default
   unless told otherwise. Wire the receiver as a Grafana contact point, referencing the existing
   `RESEND_API_KEY` secret if reused, or a new dedicated one if the alerting channel should be
   separate from transactional email (state which you picked and why).
3. Document this as a new `docs/infra/alerting-runbook.md` (what alerts exist, what threshold
   triggers each, what a responder should do first) — same "founder action" framing as
   `docs/infra/secrets-rotation-runbook.md`.
4. Manual verification: trigger one alert deliberately (e.g. temporarily scale the API
   deployment to 0 in a controlled way, confirm the alert fires, then restore it) and confirm
   the receiver actually gets the notification — don't claim this works from the YAML alone.

---

## 4. Public status page (gated on production being live)

None exists today. Minimal version: a static page (can be a single HTML file served from R2 or
Cloudflare Pages, or folded into the existing frontend as a public route — pick whichever is
less operational overhead and say why) showing current uptime for `app.tattooos.co`, sourced
from whatever §5's uptime monitor exposes as a public status feed (many free-tier uptime
monitors — including the one picked in §5 — offer a hosted public status page as part of the
same account, which is almost certainly less work than building one from scratch). **Prefer the
uptime monitor's own hosted status page over building a custom one** unless Phi has a specific
reason to want a branded in-app page — state this recommendation plainly rather than defaulting
to the more elaborate option.

---

## 5. External uptime monitoring (gated on production being live)

Nothing outside the cluster watches for an outage today. Set up a free/cheap external check
(UptimeRobot-class service or equivalent — pick one, state which and why) pointed at
`https://app.tattooos.co/health/live` (confirm this exact path is the right one — check
`Pena_e_Arte.API`'s health-check endpoint registration for the actual liveness route rather than
assuming `/health/live` is correct verbatim) at a 1–5 minute interval, alerting to the same
channel as §3's receiver. This likely needs a real account (BLOCKING-MANUAL if it requires
Phi's own email/payment details) — if so, prepare everything that doesn't need the account
(the health-check endpoint confirmed reachable, the monitoring config as a documented step) and
hand off the account-creation step explicitly.

---

## 6. Backup/DR runbook (mostly autonomous; one step is BLOCKING-MANUAL)

No `docs/infra/` backup/DR doc exists today. Three parts:
1. **Confirm DigitalOcean's actual retention window** for the existing `pena-e-arte-prod-db`
   managed MySQL cluster — check the DigitalOcean dashboard/API for the configured backup
   retention (this may need read-only API access Phi already has configured locally; if not,
   this is a BLOCKING-MANUAL lookup — ask Phi for the number rather than guessing a default).
2. **Do one real restore-to-a-scratch-instance test** — provision a throwaway DigitalOcean
   managed MySQL instance from the most recent automated backup, confirm data integrity (row
   counts on a couple of key tables, e.g. `Studios`, `Appointments`), then tear the scratch
   instance down. This is real, billable infrastructure — confirm with Phi before provisioning
   anything that costs money, even a small throwaway instance; state the expected cost in the
   final summary before doing it, not after.
3. **Decide and document an R2 bucket backup/versioning policy** — Cloudflare R2 supports
   object versioning; check whether it's enabled on the production bucket today
   (`R2_BUCKET_NAME` from the CD/secrets prompt's §5), and if not, evaluate turning it on
   (minimal cost for this bucket's likely size — portfolio images, consent-form PDFs) versus a
   scheduled export job. Recommend one, don't build both.
4. Write `docs/infra/backup-dr-runbook.md`: what backs up automatically today (DB via
   DigitalOcean, confirmed retention from step 1), what doesn't (R2, until step 3's decision is
   implemented), and the concrete "what do you do if the Hetzner box dies" procedure — re-running
   `cd.yml`'s deploy against a fresh box, restoring DNS, and the DB/R2 recovery steps from
   above. Cross-reference `docs/infra/vault-self-hosted-runbook.md` for the specific added
   wrinkle that Vault's Raft data lives only on that one box's PVC and has no backup of its own
   today — name this as a known gap in the runbook rather than silently omitting it, since it's
   real and currently true (confirmed: Vault only exists as of the CD/secrets prompt's work).

---

## 7. Secrets rotation runbook — schedule the first real drill (mostly BLOCKING-MANUAL)

`docs/infra/secrets-rotation-runbook.md` exists and has never been exercised end-to-end. This
prompt's autonomous portion: read it, confirm it's still accurate against the current secret set
(cross-check its list against the full table in `docs/claude/overnight-prompt-cd-k8s-vault-
2026-09-03.md`'s §5 — flag any drift, e.g. secrets that table lists but the runbook doesn't
mention, or vice versa) and fix any staleness found. **Do not actually rotate any real secret in
this session** — that's a real production action with real risk of an outage if done wrong, and
requires Phi to be present to catch a problem immediately. Instead: propose a specific date/time
for the first real drill in the final summary, and if this project's scheduled-task mechanism is
set up, offer to schedule a reminder for it (do not schedule anything without Phi's confirmation
first).

---

## 8. Explicitly out of scope

- Building the full GeoIP mmdb-population mechanism — §2 writes the design, doesn't implement it.
- Multi-node HA for Vault, the K3s cluster, or the database — separate, larger decisions.
- Actually rotating a secret, or actually deleting the scratch DR-test DB instance without
  confirming the restore succeeded first.
- A custom-built status page, unless Phi explicitly asks for one over the uptime monitor's
  hosted option per §4's recommendation.

---

## 9. Final self-check

- [ ] §1: either the ACME email was swapped with a confirmed real destination inbox, or this
      session explicitly states no such inbox was confirmed and nothing was changed.
- [ ] §2: the app's graceful-degradation behavior with `GeoIp:*` unset was actually verified
      (code read, not assumed), and a concrete recommended mechanism (not a bare list of
      options) is written into `docs/claude/architecture.md`.
- [ ] §3: at least one alert was deliberately triggered and confirmed to reach the chosen
      receiver — not just "the YAML looks right."
- [ ] §4/§5: if account creation was needed and this session couldn't complete it, the
      BLOCKING-MANUAL handoff states exactly what Phi needs to do and what's already prepared.
- [ ] §6: no money was spent on a scratch DR-test instance without Phi's confirmation logged in
      this session's own record of the conversation; if the test ran, its result (integrity
      confirmed or not) is in the final summary and the scratch instance was torn down.
- [ ] §7: no real secret was rotated; the runbook's accuracy was checked against the CD/secrets
      prompt's secrets table and any drift was fixed.
- [ ] Every new/changed file is committed with a clear, single-purpose commit per item (don't
      squash all seven into one commit) — matching this project's existing multi-commit
      convention (see the CD/secrets prompt's own §8 commit list for the expected granularity).
