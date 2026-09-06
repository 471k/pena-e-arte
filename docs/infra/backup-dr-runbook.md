# Backup / disaster-recovery runbook

**Owner:** Phi · **Related:** `docs/infra/vault-self-hosted-runbook.md`,
`docs/infra/secrets-rotation-runbook.md`

No backup/DR doc existed before 2026-09-05, despite this cluster now holding real production
data (see `docs/claude/project_first_production_deploy_2026_09_04.md`-equivalent memory — a real
first deploy already ran on 2026-09-04). This is the first attempt to write down what actually
backs up today, what doesn't, and what to do if the Hetzner box dies.

## What backs up automatically today

**Database — `pena-e-arte-prod-db` (DigitalOcean managed MySQL 8.4, Basic tier, 1GB RAM/1vCPU).**
**Confirmed 2026-09-05** via the dashboard's own Actions → "Restore from backup" dialog:
*"You can restore to any transaction, as far back as 7 days."* This is point-in-time recovery,
not a fixed set of daily snapshots — any moment within the last 7 days can be restored to, not
just discrete backup times. Not user-configurable on this tier (no retention setting exists to
change in Settings — the number comes from the tier itself).

## What does NOT back up automatically today

### Cloudflare R2 (portfolio images, consent-form PDFs, other uploads)

**Corrected 2026-09-05 — the original operational-hardening prompt's premise here was wrong.**
It assumed R2 supports S3-style object versioning as a toggle; checked directly against the real
`pena-e-arte-prod` bucket's Settings page (Cloudflare dashboard → R2 → bucket → Settings) and no
such feature exists there at all — the available settings are Custom Domains, CORS Policy,
Object Lifecycle Rules, **Bucket Lock Rules**, Event Notifications, Data Access Logs, On Demand
Migration, Local Uploads, and Default Storage Class. R2 does not offer per-object version history
today. The closest native feature, **Bucket Lock Rules** (currently none configured on this
bucket), is a retention/immutability lock (prevents overwrite/delete for a fixed period) rather
than version history — it protects against *deletion* but doesn't let you recover a prior
version of an object that was legitimately overwritten.

**Built 2026-09-05: `R2ExportJob`** (`Pena_e_Arte.Infrastructure/Jobs/R2ExportJob.cs`, backed by
`R2ExportService`). Daily at 06:00 UTC (Hangfire recurring job `r2-export`, staggered after
`guest-pending-upload-cleanup` at 05:00), it walks every object in the primary R2 bucket and
copies any new-or-changed one (compared by ETag) into a separate backup bucket. Downloads via
the app's existing primary-bucket credentials and uploads via a **second, dedicated** R2 token
scoped only to the backup bucket — not R2's server-side `CopyObject` API, which would need one
credential with read+write access to both buckets. Decided this way deliberately during setup:
widening the app's live primary-bucket token (which every upload/download/presigned-URL flow
depends on) to also cover a brand-new backup bucket was judged higher-risk than the bytes simply
passing through the job process once a day for images/PDFs of this size. **Deliberately never
deletes from the backup bucket**, even when the source object is deleted — propagating a
deletion into the backup would defeat the entire point of having one. This means the backup
bucket grows without bound as objects are deleted from production; revisit with an Object
Lifecycle Rule on the *backup* bucket (e.g. expire backup copies after N days past last-modified)
if that growth becomes a real cost concern — not needed at current scale.

**Status: fully live and verified 2026-09-05.**
1. ✅ Created the `pena-e-arte-prod-backup` R2 bucket (Standard storage class, Automatic/Eastern
   Europe location — matching the primary bucket, Public Access disabled).
2. ✅ Created a dedicated Account API token (`pena-e-arte-prod-backup`), "Object Read & Write",
   scoped to that bucket only — never the primary bucket, and never reusing the app's existing
   R2 token.
3. ✅ Set `R2_BACKUP_BUCKET_NAME`, `R2_BACKUP_ACCESS_KEY_ID`, `R2_BACKUP_SECRET_ACCESS_KEY` as
   GitHub Actions secrets, redeployed.
4. ✅ Verified via a real manual trigger (Hangfire dashboard → Recurring Jobs → `r2-export` →
   Trigger now — only possible after also fixing a separate, previously-undiscovered bug where
   `/hangfire` was never reachable via the public domain at all, see
   `frontend/nginx.conf.template`'s history). Log line: `R2ExportJob completed: 0 copied, 0
   already up to date, 0 failed`. Confirmed this is the *correct* result, not a bug — the
   production R2 bucket is genuinely empty (the app only went live 2026-09-04, no real
   studio/client has uploaded a portfolio image or signed a consent form in production yet).
   Re-run this trigger once real objects exist to confirm the actual copy path, not just the
   empty-bucket path.

If literally every object in a run fails to copy (as opposed to occasional per-object flakiness),
`R2ExportService` deliberately throws rather than swallowing it — this surfaces as a real
Hangfire `Failed` job via `HangfireJobFailureLogFilter`, which the Hangfire-failure-rate alert in
`docs/infra/alerting-runbook.md` catches. A systemic problem (bucket deleted, token lost access)
pages someone; it does not silently degrade to warning-level logs nobody watches.

### Vault's Raft/boltdb state

`vault-0`'s PVC (`tattooos` namespace) is real, unbacked-up data as of 2026-09-05 — `vault-0`
has already been through one real cold boot on this cluster (initially OOMKilled at a 256Mi
limit, confirmed via `kubectl describe`, then redeployed at 384Mi/192Mi; see
`docs/infra/vault-self-hosted-runbook.md`), so whatever secrets are stored there right now exist
in exactly one place. Losing this PVC means re-running the entire init/unseal runbook from
scratch and re-populating every secret Vault holds — named here as a known gap, not silently
omitted. No fix implemented in this pass; closing it (e.g. Raft's built-in snapshot support, or
a periodic `vault operator raft snapshot save` into R2) is future work, not in scope for this
runbook.

## Restore test — procedure (not yet run)

Per Phi's decision on 2026-09-05, this session documented the procedure without executing it —
scheduled as a deliberate future drill rather than run blind today. To actually run it:

1. In the DigitalOcean dashboard, go to the `pena-e-arte-prod-db` cluster → find its most recent
   automated backup (Settings → Backups, or via `doctl databases backups list <cluster-id>` if
   `doctl` is authenticated).
2. Create a new, separate managed MySQL cluster restored from that backup — DigitalOcean's
   "Restore to a new cluster" action does this without touching the live `pena-e-arte-prod-db`.
   Use the smallest available tier (Basic, 1GB/1vCPU) — this is a throwaway integrity check, not
   a load test. Expected cost: DigitalOcean bills managed databases hourly: at Basic-tier
   pricing (~$15/month ≈ ~$0.02/hour), a test lasting under an hour costs well under $1 — confirm
   the exact rate shown in the dashboard before provisioning, since pricing can change.
3. Connect to the restored instance and confirm data integrity: row counts on `Studios` and
   `Appointments` at minimum (`SELECT COUNT(*) FROM Studios; SELECT COUNT(*) FROM Appointments;`)
   compared against the same query on the live production database from around the backup's
   timestamp.
4. **Tear down the scratch instance immediately after confirming** (or after confirming failure
   — either way, don't leave a second billed database cluster running).
5. Record the result (pass/fail, and the date) somewhere durable — this runbook's own git
   history is fine for that, since it carries no secret material.

## What to do if the Hetzner box dies

1. **Provision a replacement box** (same Hetzner project, or a new one if the original account/
   region is unavailable) and note its new public IP.
2. **Re-run the K3s bootstrap** — cluster install, cert-manager (`v1.21.1`, pinned — see
   `docs/claude/architecture.md`'s Decisions Log), and the self-hosted GitHub Actions runner
   (`docs/infra/self-hosted-runner-setup.md` — this step needs Phi's own interactive SSH session
   with the passphrase-protected key; no Claude Code session can do this step).
3. **Update DNS** — Cloudflare DNS records for `app.tattooos.co` / `staging.tattooos.co` /
   whatever else points at the old box's IP need to point at the new one. If using Cloudflare
   Tunnel for anything (see `reference_test_tattooos_deployment` — the old test-domain tunnel
   setup, separate from the real production Ingress), that tunnel's config also needs the new
   box's local target updated.
4. **Re-run `cd.yml`'s `deploy` job** (or trigger it manually) against the new box once the
   self-hosted runner is registered on it — this recreates every K8s resource, Secret, and the
   `letsencrypt-prod-dns01` ClusterIssuer from scratch, since none of that lives outside git
   + GitHub Actions secrets (the DB and R2 recovery below are the two things `cd.yml` alone does
   NOT restore).
5. **Restore the database** — DigitalOcean managed MySQL isn't on the Hetzner box at all, so it
   survives the box dying untouched. No action needed here unless DigitalOcean itself is also
   affected, in which case use the restore procedure above against the most recent backup.
6. **Restore R2** — same story: Cloudflare R2 is independent of the Hetzner box and survives its
   loss untouched. This step is about a *box* failure, not an R2 data-loss scenario — R2 itself
   isn't hosted on the Hetzner box at all — so no action is needed here specifically; the R2
   export job above (once its BLOCKING-MANUAL step is done) is what covers accidental object
   loss, a separate failure mode from this section.
7. **Restore Vault** — this is the one piece of real state that lives only on the dead box's
   PVC, per the known gap named above. Until a Raft snapshot backup exists, this step is
   "re-run the init/unseal runbook and re-populate every secret it held from the GitHub Actions
   secrets that are the actual source of truth for all of them" — Vault today is a cache/proxy
   in front of those GitHub secrets (see `docs/infra/ADR-0002-secrets-management.md`), not the
   only copy, so this is recoverable, just manual and not yet automated.
