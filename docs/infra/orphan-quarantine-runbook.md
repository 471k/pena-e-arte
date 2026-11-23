# Orphaned-object quarantine runbook (sweep Phase 3)

**Owner:** Phi · **Code:** `Pena_e_Arte.Infrastructure/Jobs/OrphanedObjectSweepJob.cs` ·
**Related:** `docs/infra/backup-dr-runbook.md`, `docs/claude/overnight-prompt-orphaned-object-cleanup-2026-11-15.md`

## What the job does

`orphaned-object-sweep` runs daily at 05:30 UTC, after `guest-pending-upload-cleanup` (05:00). It lists the
bucket, reads every column that can hold a stored file's address, and picks out objects that are **older than
7 days, not under an excluded prefix (`consent/`, `reports/industry/`) and pointed at by no row.**

It has two modes, chosen by the setting `OrphanedObjectSweep:Mode` (env var `OrphanedObjectSweep__Mode`):

| Mode | Setting | What happens to an unreferenced object |
|---|---|---|
| **Report-only** (default) | unset, empty, `ReportOnly`, or **any unknown value** (logged as a warning) | Nothing. One log line per owner/area, one summary line |
| **Quarantine** | `Quarantine` | Copied server-side to `_quarantine/<yyyy-MM-dd>/<original key>`, the copy's size read back and compared, and only then the original deleted |

There is **no hard-delete mode**. Emptying the quarantine is Phase 4, a separate decision.

**In quarantine mode a step that fails stops that object's move:**
- **Copy fails:** the original stays where it was.
- **The copy doesn't read back at the original's size:** the original stays.
- **The delete of the original fails:** the original stays, and a copy also sits in quarantine.

Each failure is a warning line with the owner, area and S3 error code, never the key. If **every** move fails,
the job fails (Hangfire marks it Failed) after logging its summary.

**The brake.** Quarantine mode moves nothing and logs an **error** line `(quarantine, refused)` when:
- the reference reader returned no keys at all, or
- at least 20 objects are candidates and more than half of them look unreferenced.

A reader that silently read too little would otherwise condemn live files in bulk. Production holds 6 objects,
so the share rule can't fire there today.

## Why `_quarantine/` is at the top level

The presign route (`GetPresignedUploadUrlQuery`) lets a caller choose any folder under `{studioId}/`. A
quarantine under a studio id would be one users can write into. `_quarantine/` sits at the bucket root, which
only this job writes.

The job **never lists `_quarantine/`**: `IR2Service.ListAllExceptAsync` lists the top level with a delimiter
and skips that folder at the list call. So quarantined objects are never re-condemned and cost the daily
listing no memory. The summary line ends `_quarantine/ not listed` to say so.

## Turning quarantine on (per environment, staging first)

**The order matters: lock first, then the switch.**

1. **Lock rule on the bucket's `_quarantine/` prefix, 30 days.** A Cloudflare account setting (dashboard → R2
   → bucket → Settings → Bucket Lock Rules, or the API's `PUT /accounts/{account}/r2/buckets/{bucket}/lock`).
   Rule: prefix `_quarantine/`, condition Age, 2,592,000 seconds (30 days), enabled.
   - Production bucket: **`pena-e-arte-prod`**. Staging: `pena-e-arte-staging`.
   - Read the rules back afterwards. Today both buckets read `[]`.
   - **Production needs Phi's go**, given with the current value read back first.
   - **Verified on staging (23 Nov 2026):**
     - Lock rules are prefix-scoped, so the lock never touches live files outside `_quarantine/`.
     - A copy's lock age counts from the copy, not from the original upload.
     - A DELETE of a locked object returns 409.
     - Removing the rule unlocks at once.
2. **The switch:** add `OrphanedObjectSweep__Mode: "Quarantine"` to that environment's
   `k8s/overlays/<env>/api-config-patch.yaml` in a PR. CD applies ConfigMaps and restarts the API, so the next
   05:30 UTC run uses it. This is a production-config change: hold it for Phi on production.
3. **Before the run, write the prediction** from a fresh listing, using the same fields as the summary line
   (listed, excluded, younger, referenced, unreferenced, bytes).
4. **After the run, read the line from Loki with a raw filter.** `| json` silently matches nothing here,
   because of the container runtime's prefix:
   ```
   {namespace="tattooos", container="api"} |= "OrphanedObjectSweepJob ("
   ```
   (`tattooos-staging` for staging.) Compare it with the prediction, field by field. Then check the bucket:
   - each moved original is gone;
   - each `_quarantine/<date>/<key>` exists at the same size;
   - a DELETE of one quarantined copy returns 409. That checks the lock, not the job.

**Turning it off:** remove the line (or set `ReportOnly`) in a PR. The job goes back to report-only.
Quarantined objects stay where they are until their lock expires and Phase 4 decides what to do with them.

## Reading the summary line

```
OrphanedObjectSweepJob (quarantine) listed L object(s): E excluded, Y younger than 7 days, R referenced,
U unreferenced (B bytes); moved M (MB bytes) to _quarantine/<date>/, C copy failed, V copy not verified,
D copied but original kept; hard-deleted 0. References: ...; _quarantine/ not listed
```
- **Every run names its mode** in the parentheses: `report-only`, `quarantine`, or `quarantine, refused`.
- **The report-only lines are unchanged since Phase 2,** so earlier predictions still read against them.
- **`U = M + C + V + D`** on every quarantine run that isn't refused.

## Restoring an object from quarantine

Restore when a quarantined object turns out to be needed: a row still points at its original address (the
reader got it wrong), or someone asks for a removed file back.

1. **Switch the environment to report-only first** if the object is still unreferenced (step 2 above, in
   reverse). Otherwise the next run moves it straight back into quarantine. A restore for the "reader got it
   wrong" case also needs the reader fixed before quarantine is switched back on.
2. **The original key** is the quarantine key with the `_quarantine/<yyyy-MM-dd>/` part removed:
   `_quarantine/2026-11-30/<studioId>/portfolio/<artistId>/x.jpg` → `<studioId>/portfolio/<artistId>/x.jpg`.
3. **Copy it back, server-side**, to the original key. Use an S3 CopyObject inside the same bucket: the in-pod
   `curl --aws-sigv4` helper using the pod's own `CloudflareR2__*` variables (never printed), or any S3 client
   with the app's token. Content type and metadata come with the copy.
4. **Check:**
   - the restored object's size equals the quarantine copy's;
   - the public address `https://media.tattooos.co/<original key>` returns 200 (portfolio images only;
     private files are signed reads, so check them through the app's own screen instead);
   - the screen that shows it renders it.
5. **Leave the quarantine copy alone.** It is locked, and it is the undo for the restore.

## What "our code can't delete from quarantine" means, and erasure

- **The lock is a Cloudflare account permission.** The app's R2 tokens are object-level and can't change lock
  rules. So **our code can't delete from quarantine while the rule is on. Someone with Cloudflare lock
  permission can,** by removing the rule, which unlocks every quarantined object at once.
- **Erasure requests:** if a client asks to be erased and one of their files is in quarantine, deleting it
  needs **a person with Cloudflare lock permission**, not just the app:
  1. remove the rule;
  2. delete the object;
  3. put the rule back.

  This is a process requirement, not a conflict with the one-month response window. It is part of the open
  Legal question about storage locations (the live bucket, backups, logs, `_quarantine/`, and saved drafts if
  those are ever built). **Don't shorten the 30 days to make this easier:** the recovery window is the point.
- **Backups:** `r2-export` (06:00 UTC) copies the whole bucket into `pena-e-arte-prod-backup` and never deletes
  there. So the backup already holds the original key from before the move, and it will also hold the
  quarantine copy. Quarantine changes nothing about the backup's erasure question.

## For Phase 4 (not built): a named requirement

**Any batch delete must read per-key results.** On a locked object, R2's `DeleteObjects` returns **HTTP 200**
with a per-key `ObjectLockedByBucketPolicy` error. A job that checks only the status code reports success while
deleting nothing. Phase 3 uses single-object deletes and counts each one.
