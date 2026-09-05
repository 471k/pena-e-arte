# Overnight Prompt — CD Pipeline, Missing Production Secrets, and Self-Hosted In-Cluster Vault

> Feed this file directly to Claude Code (running in the main **Pena e Artë - Engineering**
> project, with full repo write access) as the task prompt, **alongside**
> `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md`. Read both files in full
> before writing anything. **This file does not repeat that file — it amends it.** Phases 1–7,
> 9 (except §4 below, which corrects part of it), and 10 of the July 26 prompt are unchanged
> and remain authoritative. This file specifies, precisely: (1) the current Phase-0 status as
> of today, (2) a full replacement for that prompt's Phase 8, (3) a correction to a real bug in
> its Phase 9, (4) the complete list of GitHub Actions secrets still missing, and (5) the
> Decisions Log / ADR updates that follow from all of the above. **Mode: fully autonomous**,
> except the items marked **BLOCKING-MANUAL** below — those require Phi personally, cannot be
> done by an unattended session, and must already be complete before this session runs the
> corresponding phase. If a BLOCKING-MANUAL prerequisite is missing, stop and say so rather
> than improvising around it — same standard the July 26 prompt already holds itself to.

**Date logged:** 2026-09-03
**Requested by:** Phi
**Origin:** Engineering-consultation follow-up. Three real gaps remained after the July 26 K3s
deploy prompt was written: (1) CI still doesn't push or deploy anything — no `cd.yml` exists;
(2) most of the GitHub Actions secrets that prompt's own Phase 0 step 6 and Phase 9 depend on
were never added (only 4 of the ~20 real ones exist today); (3) that prompt's Phase 8 specified
plain GitHub-Secrets-to-K8s-Secrets, but a *later* decision — ADR-0002's production-backend
resolution on 1 Aug 2026 — named **HCP Vault** as the production secrets backend, which Phase 8
never got updated to reflect. Investigating gap (3) during this consultation surfaced a fourth,
previously unknown problem: HCP Vault Secrets (the cheap, simple option) shut down 1 July 2026,
and the only remaining HCP-managed option compatible with this app's existing `VaultSharp` code
is HCP Vault Dedicated, starting at roughly **$1,150–1,200/month** plus per-client fees — wildly
disproportionate for a mechanism (`ISecretsProvider`/`VaultSecretsProvider`) that nothing in the
app calls yet. Phi's decision, made in this consultation session: **do not pay for a managed
Vault.** Self-host Vault inside the existing K3s cluster instead, with real persistent storage
(not dev mode), accepting reduced availability (no auto-unseal, no HA) as an explicit tradeoff
for zero additional monthly cost. This prompt implements that.

**Checkpoint before starting:**
```bash
git status                     # must be clean before starting
git checkout main && git pull
git checkout -b feat/cd-secrets-inline-vault
git commit --allow-empty -m "checkpoint: before CD/secrets/in-cluster-Vault work"
```

---

## 1. Updated Phase 0 status (supersedes §0.1 of the July 26 prompt as of today)

Steps 1–4 of the July 26 prompt's Phase 0 are done and unchanged (Hetzner K3s box, DigitalOcean
managed MySQL, Cloudflare API token, Cloudflare DNS record + Full-strict SSL mode) — see that
file's §0.1 for the recorded details (server IPs, cluster names, etc.); this prompt does not
repeat them.

**New since that file was written:** cluster connectivity was confirmed working today
(2026-09-03) after a Hetzner firewall fix — `kubectl` can now reach the box. This does not
change any of §0.1's recorded values; it only confirms the box that was provisioned 2026-07-27
is actually reachable, which the original prompt's Phase 0 completion criteria required but
which had not been separately confirmed on record until now.

**Step 5 (GHCR permissions)** — status not confirmed either way. **BLOCKING-MANUAL**: before
running §4 (CD workflow) below, Phi must confirm `Settings → Actions → General → Workflow
permissions` has `packages: write` enabled for `471k/pena-e-arte` (or provision a GHCR PAT if
not). If this session finds `cd.yml`'s `build-and-push` job failing on a permissions error
against GHCR, stop and flag it back rather than attempting to work around it with a personal
access token pasted into a workflow file.

**Step 6 (GitHub Actions secrets)** — **only 4 of the secrets this deployment needs exist
today**: `KUBE_CONFIG`, `PROD_DB_CONNECTION_STRING`, `CLOUDFLARE_API_TOKEN`, `JWT_SECRET_KEY`.
§5 below is the complete, corrected list of what's still missing. **This is the single largest
BLOCKING-MANUAL item in this prompt** — every one of these is a real external credential (a
live Stripe key, a Resend API key, etc.) that only Phi can generate, and per the July 26
prompt's own standing rule, **never paste real secret values into a chat, prompt, or doc
anywhere, including back to this consultation project — add them directly in the GitHub
Settings UI.**

---

## 2. Decision reversal — production secrets backend is self-hosted in-cluster Vault, not HCP Vault

This section is the actual content that goes into `docs/infra/ADR-0002-secrets-management.md`
(as a new "Addendum" section, appended — do not delete or rewrite the original ADR body, this
is a dated amendment, matching how `overnight-prompt-k3s-production-deploy-2026-07-26.md` §2.8
handled its own same-day reversal on the ingress-controller choice) and into
`docs/claude/architecture.md`'s Decisions Log row for "Per-tenant secrets: ISecretsProvider..."
(amend that row's "Choice" cell with a new bolded sentence, same convention already used there
for the 1 Aug 2026 HCP Vault resolution — do not delete the existing text, append to it).

**Addendum — 3 Sep 2026:** ADR-0002's 1 Aug 2026 resolution named HCP Vault (HashiCorp-managed)
as the production secrets backend without pricing it concretely. Investigating this gap during
an engineering-consultation session found: (a) **HCP Vault Secrets** — the low-cost, KV-only
managed product, and the one implicitly assumed by the "managed service, paid" framing in
ADR-0002's original comparison table — **shut down 1 July 2026** and is no longer available at
any price. (b) The only remaining HCP-managed product compatible with this app's existing
`VaultSharp`/`TokenAuthMethodInfo`/KV-v2 integration is **HCP Vault Dedicated**, which starts at
approximately **$1,150–1,200/month for the smallest production tier, plus ~$73/month per
authenticated client** — roughly 75–80x the cost of the managed MySQL instance this same
deployment already uses, for a mechanism (`ISecretsProvider`) that no code path calls yet (it
backs `StudioCredentialRef`, ADR-0001 Article 4(g) per-tenant credential scaffolding, which is
not built). HCP Vault Dedicated also has a cheap "Development" tier, but HashiCorp's own docs
say explicitly not to use it for production workloads.

**Revised decision:** run Vault **self-hosted inside the existing K3s cluster** instead of any
HCP-managed product. Unlike when ADR-0002 originally rejected self-hosted Vault ("disproportionate
ops burden for a solo founder" — true at the time, when no cluster existed at all), a K3s
cluster now exists and already runs comparable single-node stateful workloads (the observability
stack's Prometheus/Loki/Tempo, each with their own PVC). The specific ops burden ADR-0002 named
— unsealing, HA, backup — is accepted here as an **explicit, named tradeoff, not silently
dropped**: this deployment runs Vault with real Raft-backed persistent storage (data survives
pod restarts, unlike the local `docker-compose.yml` dev-mode service), a **single node, manually
unsealed** (no cloud-KMS auto-unseal — that would reintroduce both cost and a new external
dependency this decision is explicitly trying to avoid), with **no high availability**. Concretely:
after any Vault pod restart (crash, node reboot, manual redeploy), Vault re-seals and every
`ISecretsProvider` call fails closed until a human runs `vault operator unseal` three times
against the pod (§3.3 below). **This has zero functional impact today** — confirmed again, as
of this writing, that nothing in the codebase calls `ISecretsProvider` in a live request path —
but it is a real operational gap that must be resolved (auto-unseal via a cloud KMS, or a
documented on-call runbook) before the per-tenant-credentials feature this mechanism exists for
actually ships and starts depending on Vault being reachable. Named here as a following-up
requirement, not silently deferred.

No `VaultSharp` code changes — same as ADR-0002's original point about HCP Vault: only
`Vault:Address`/`Vault:Token` config differs. `Vault:Address` becomes the in-cluster Service DNS
name (§3.1); `Vault:Token` becomes a scoped, non-root token generated during the manual init
runbook (§3.3), never the root token.

---

## 3. Phase 8 replacement (full replacement of the July 26 prompt's Phase 8 section)

### 3.1 — App-level secrets: unchanged from the original Phase 8

Every app-level secret the API reads today (JWT, Stripe, Cloudflare R2, Resend, Twilio,
Instagram, the social-OAuth set, Hangfire dashboard creds, the DB connection string) is read via
plain `IConfiguration` / environment variables — **not** through `ISecretsProvider`, which
nothing in the app calls yet (confirmed against `InfrastructureServiceExtensions.cs`: "Registered
always — nothing consumes it yet"). Routing these through Vault would mean writing a new
Vault-backed `IConfigurationSource`, or a CD step that resolves them from Vault before writing
K8s Secrets — real new Application/Infrastructure-layer work, out of this prompt's infra-only
scope boundary (§4 of the July 26 prompt: "No changes to `Pena_e_Arte.Domain/`,
`Pena_e_Arte.Contracts/`, or any endpoint/handler file"). **Named as a real future option, not
built here**: if per-tenant secrets (§3.2 below) get built on `ISecretsProvider` later, revisit
whether app-level secrets should move onto the same mechanism for consistency — a decision for
whoever specs that feature, not this prompt.

So: build exactly what the July 26 prompt's Phase 8 already specified for these — two objects,
populated at deploy time by CD from GitHub Actions secrets (`kubectl create secret generic ...
--from-literal=... --dry-run=client -o yaml | kubectl apply -f -`):
- `pena-e-arte-api-secrets` (`type: Opaque`) — JWT, Stripe, R2, Resend, Twilio, Instagram, the
  social-OAuth set, Hangfire creds, `PROD_DB_CONNECTION_STRING`. **Add two new keys to this same
  Secret**: `Vault__Address` and `Vault__Token`, sourced from new GitHub secrets `VAULT_ADDR`
  and `VAULT_TOKEN` (§3.3) — this is what makes ADR-0002's "Vault is the production secrets
  backend" real for the `ISecretsProvider` mechanism, even though nothing calls it yet.
- `pena-e-arte-api-config` (plain `ConfigMap`) — `Jwt__Issuer`, `Jwt__Audience`, `App__BaseUrl`,
  `Cors__AllowedOrigins__0`, `Migrations__ApplyOnStartup` — unchanged.
- `cloudflare-api-token` — unchanged, from Phase 6 of the July 26 prompt.

### 3.2 — Vault itself: new in-cluster manifests

Add to `k8s/base/` (alongside the structure the July 26 prompt's Phase 1 already scaffolds):

```
k8s/base/
  vault-statefulset.yaml      # StatefulSet, not Deployment — needs stable network identity +
                               # a stable PVC across restarts for Raft storage
  vault-service.yaml          # ClusterIP, port 8200, name pena-e-arte-vault — internal only,
                               # NOT on the public Ingress (matches Phase 7's Grafana precedent:
                               # an admin-capable surface with no considered public-auth story
                               # doesn't get exposed by default)
  vault-configmap.yaml        # server config (HCL or JSON): storage "raft", listener "tcp"
                               # with tls_disable = true (internal-cluster-only traffic; the
                               # July 26 prompt's Ingress/cert-manager work is not extended to
                               # this internal-only Service — flag if this should change)
```

`StatefulSet`, single replica, image `hashicorp/vault:1.18` (same tag as the local
`docker-compose.yml` dev-mode service, per this project's existing "pin the same tag as compose"
convention from the July 26 prompt's §2.3). `volumeClaimTemplates` for a `/vault/data` PVC (raft
storage — size `1Gi` is a reasonable starting point, same conservative-default reasoning as the
July 26 prompt's Redis PVC). No `readinessProbe` marking the pod ready until unsealed is
correct default Vault behavior — do not override it; a sealed Vault correctly reports
not-ready, which is the right signal for anything that might depend on it later.

**Do not attempt automated unsealing, cloud-KMS auto-unseal, or Shamir key storage anywhere in
this manifest set.** That is real, security-sensitive follow-up work (§6), not scope-creep into
tonight's prompt.

### 3.3 — Vault initialization and unsealing — BLOCKING-MANUAL, Phi only, one time ever

`vault operator init` is destructive to get wrong: it generates the root token and unseal key
shares exactly once, and losing them means the data is unrecoverable short of wiping and
reinitializing. **This session must not run it, must not see the output, and must not be asked
to relay the values anywhere** — this is the same "never paste a real secret into a chat/doc"
rule from Phase 0, applied to key material that's even more sensitive than an API key. Write
this exact runbook as a new file, `docs/infra/vault-self-hosted-runbook.md` (same "founder
action" framing as `docs/infra/secrets-rotation-runbook.md`), but do not execute any of its
steps:

```bash
# Run by Phi, directly against the cluster, after the StatefulSet in §3.2 is deployed and the
# vault-0 pod is Running (but Not Ready — sealed, expected):

kubectl exec -it vault-0 -n pena-e-arte -- vault operator init -key-shares=5 -key-threshold=3
# Prints 5 unseal key shares and a root token. Record all 6 values somewhere durable and NEVER
# in git, chat, or any doc in this repo — a password manager or printed-and-locked-away copy,
# same standard as any other production root credential.

# Unseal (needs 3 of the 5 key shares — run this command 3 times with 3 different shares):
kubectl exec -it vault-0 -n pena-e-arte -- vault operator unseal

# Authenticate with the root token, then create a scoped, non-root token for the app to use —
# never put the root token itself into a GitHub secret:
kubectl exec -it vault-0 -n pena-e-arte -- vault login <root-token>
kubectl exec -it vault-0 -n pena-e-arte -- vault secrets enable -path=secret kv-v2
kubectl exec -it vault-0 -n pena-e-arte -- vault policy write pena-e-arte-app - <<'EOF'
path "secret/data/*" {
  capabilities = ["read"]
}
EOF
kubectl exec -it vault-0 -n pena-e-arte -- vault token create -policy=pena-e-arte-app -period=720h
# Record the resulting token — this is the value for the VAULT_TOKEN GitHub Actions secret
# (§5). -period=720h makes it a renewable periodic token (30 days) rather than one with a hard
# expiry; nothing currently renews it automatically — a follow-up (§6), not solved here.

# GitHub Actions secrets to add from this session:
#   VAULT_ADDR = http://pena-e-arte-vault.pena-e-arte.svc.cluster.local:8200
#   VAULT_TOKEN = <the scoped token from vault token create above, NOT the root token>
```

**After any Vault pod restart**, the `vault operator unseal` step above must be repeated by hand
before Vault is usable again — the runbook states this explicitly, with the same "confirmed
today, zero functional impact since nothing calls `ISecretsProvider` yet, but a real gap before
per-tenant credentials ship" framing as §2's addendum.

---

## 4. Phase 9 correction — the July 26 prompt's `cd.yml` spec has a real bug

The July 26 prompt's Phase 9 says the `build-and-push` job uses "the same build-args the
existing `docker-build` CI job already uses." That CI job (`ci.yml`) deliberately uses
placeholder values — confirmed in the live workflow file:
```yaml
build-args: |
  VITE_STRIPE_PUBLISHABLE_KEY=pk_test_placeholder
  VITE_CONTACT_EMAIL=support@tattooos.co
  VITE_GOOGLE_CLIENT_ID=placeholder
  VITE_APPLE_CLIENT_ID=placeholder
  VITE_PUBLIC_URL=http://localhost:8081
```
— correct for a build-only validation job that pushes nothing, wrong if reused verbatim for the
image `cd.yml` actually deploys: the shipped frontend bundle would silently bake in a fake
Stripe publishable key (breaking checkout), fake Google/Apple OAuth client IDs (breaking social
login), and `http://localhost:8081` as its own public URL. The build would succeed and the
rollout would go green — this would fail silently at runtime, exactly the kind of gap this
project's rules say to catch rather than let ship. **Correct `cd.yml`'s `build-and-push` job**
to pass real values, sourced from new GitHub Actions secrets (§5):
```yaml
build-args: |
  VITE_STRIPE_PUBLISHABLE_KEY=${{ secrets.VITE_STRIPE_PUBLISHABLE_KEY }}
  VITE_CONTACT_EMAIL=support@tattooos.co
  VITE_GOOGLE_CLIENT_ID=${{ secrets.VITE_GOOGLE_CLIENT_ID }}
  VITE_APPLE_CLIENT_ID=${{ secrets.VITE_APPLE_CLIENT_ID }}
  VITE_PUBLIC_URL=https://app.tattooos.co
```
These four are client-exposed values (baked into the shipped JS bundle — visible to anyone who
views source), so GitHub Actions "repository variables" would be the more semantically correct
place for the three that are genuinely public (publishable key, both OAuth client IDs) rather
than "secrets." Using secrets for all of them anyway is simpler and matches this deployment's
existing uniform pattern — a minor judgment call, flagged rather than silently picked either way.

Everything else in the July 26 prompt's Phase 9 (the `workflow_run` trigger keyed on `ci.yml`,
the migration-Job-before-rollout ordering, `kubectl rollout status` gating) is unchanged.

---

## 5. Complete GitHub Actions secrets checklist (supersedes the partial lists in the July 26 prompt's §0 step 6 and §9)

Naming convention used below matches what's already live: `PROD_` prefix only where already
established (`PROD_DB_CONNECTION_STRING`, and the not-yet-added `PROD_GRAFANA_ADMIN_USER`/
`PROD_GRAFANA_ADMIN_PASSWORD`, per the July 26 prompt's own naming), bare `.env.example` names
everywhere else (matching the already-live `CLOUDFLARE_API_TOKEN`, `JWT_SECRET_KEY`,
`KUBE_CONFIG`). Do not invent a different convention.

| GitHub secret name | Status today | Required for | Notes |
|---|---|---|---|
| `KUBE_CONFIG` | **exists** | cluster access | — |
| `PROD_DB_CONNECTION_STRING` | **exists** | app startup | must include `SslMode=Required` per §0.2 of the July 26 prompt |
| `CLOUDFLARE_API_TOKEN` | **exists** | TLS issuance (Phase 6) | — |
| `JWT_SECRET_KEY` | **exists** | app startup | **verify it's a real generated value, not the `.env.example` dev placeholder, before relying on it** — a 32+ byte value from `openssl rand -base64 48` per the rotation runbook |
| `STRIPE_SECRET_KEY` | missing | Flow B billing | live-mode key |
| `STRIPE_PUBLISHABLE_KEY` | missing | Flow B billing + frontend build (§4) | not secret, but needed |
| `STRIPE_WEBHOOK_SECRET_BILLING` | missing | Flow B billing | — |
| `STRIPE_WEBHOOK_SECRET_CONNECT` | missing | Flow B billing | — |
| `R2_ACCOUNT_ID` | missing | file storage (portfolio images, consent forms, etc.) | not secret |
| `R2_ACCESS_KEY_ID` | missing | file storage | — |
| `R2_SECRET_ACCESS_KEY` | missing | file storage | — |
| `R2_BUCKET_NAME` | missing | file storage | not secret |
| `R2_PUBLIC_URL` | missing | file storage | not secret |
| `RESEND_API_KEY` | missing | email notifications | — |
| `RESEND_FROM_ADDRESS` | missing | email notifications | not secret |
| `TWILIO_ACCOUNT_SID` | missing | SMS reminders | not secret, but functionally required for a Vagaro/Fresha-tier feature (CLAUDE.md rule 6) |
| `TWILIO_AUTH_TOKEN` | missing | SMS reminders | — |
| `TWILIO_FROM_NUMBER` | missing | SMS reminders | not secret |
| `INSTAGRAM_APP_ID` / `INSTAGRAM_APP_SECRET` / `INSTAGRAM_REDIRECT_URI` | missing | optional — Instagram portfolio sync | app stays config-gated (inactive) if left empty; not launch-blocking |
| `INSTAGRAM_TOKEN_ENCRYPTION_KEY` | missing | required only if the above are set | self-generated |
| `SOCIAL_STATE_SIGNING_KEY` | missing | optional — social OAuth Connect flows | self-generated, base64 32-byte |
| `TIKTOK_CLIENT_KEY` / `TIKTOK_CLIENT_SECRET` / `TIKTOK_REDIRECT_URI` | missing | optional — social verification | config-gated if empty |
| `FACEBOOK_APP_ID` / `FACEBOOK_APP_SECRET` / `FACEBOOK_REDIRECT_URI` | missing | optional — social verification | config-gated if empty |
| `X_CLIENT_ID` / `X_CLIENT_SECRET` / `X_REDIRECT_URI` / `X_BEARER_TOKEN` | missing | optional — social verification | config-gated if empty |
| `YOUTUBE_CLIENT_ID` / `YOUTUBE_CLIENT_SECRET` / `YOUTUBE_REDIRECT_URI` / `YOUTUBE_API_KEY` | missing | optional — social verification | config-gated if empty |
| `HANGFIRE_DASHBOARD_USERNAME` / `HANGFIRE_DASHBOARD_PASSWORD` | missing | app startup (guarded, no `admin`/`admin`) | self-generated |
| `PROD_GRAFANA_ADMIN_USER` / `PROD_GRAFANA_ADMIN_PASSWORD` | missing | observability (Phase 7) | self-generated |
| `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR` | missing | correctness (Phase 10) | value already known: `10.42.0.0/16`, confirmed against the real cluster per the July 26 prompt's §0.1 |
| `VAULT_ADDR` | missing | wires `ISecretsProvider` in prod (§3.1) | in-cluster Service DNS name, not a real secret but kept alongside `VAULT_TOKEN` for consistency |
| `VAULT_TOKEN` | missing | wires `ISecretsProvider` in prod (§3.1) | **BLOCKING-MANUAL** — only exists after §3.3's runbook runs; a scoped token, never the root token |
| `VITE_GOOGLE_CLIENT_ID` / `VITE_APPLE_CLIENT_ID` | missing | frontend build (§4) | real OAuth client IDs, not the CI placeholder |

**Required for the app to start at all**: `PROD_DB_CONNECTION_STRING`, `JWT_SECRET_KEY`,
`HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD`, `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR`. **Required for
a real launch at current feature parity** (not startup-blocking, but a broken/missing core
feature if skipped): Stripe set, R2 set, Resend set, Twilio set, Grafana admin set, the two
`VITE_*` OAuth IDs. **Genuinely optional, safe to leave empty at launch**: the entire
Instagram/TikTok/Facebook/X/YouTube social-verification block — the app already handles these
being unset via its config-gating pattern, confirmed in `.env.example`'s own comments.

---

## 6. Explicitly out of scope after this prompt

- **Vault auto-unseal** (cloud KMS transit or similar) and **Vault HA** (multiple raft nodes) —
  named in §2/§3.3 as a real gap, not solved here; solo-node manual-unseal is the accepted
  tradeoff for zero added monthly cost.
- **Routing app-level secrets (Stripe, R2, etc.) through `ISecretsProvider`/Vault** — §3.1
  explains why; would need new Application/Infrastructure-layer code, not an infra-only change.
- **Vault policy design beyond the single app-wide read-only policy in §3.3** — real per-tenant
  path scoping (e.g., a policy per studio) is `ISecretsProvider`/`StudioCredentialRef` follow-up
  work that hasn't been built yet, matching ADR-0002's own "Not in scope" section.
- Everything the July 26 prompt's own §13 already named out of scope (alerting/on-call,
  public status page, retention tuning, autoscaling, multi-node HA, DB backup/restore runbook)
  remains out of scope here too — this prompt doesn't expand into any of it.

---

## 7. Test requirements

No new application-code tests beyond what the July 26 prompt's Phase 4/10 already specify —
this prompt's changes are infra manifests, a workflow-file correction, and two new docs. The
`Vault__Address`/`Vault__Token` config keys added to `pena-e-arte-api-secrets` (§3.1) don't need
a new test: `VaultSecretsProvider`'s construction doesn't connect (confirmed in its own source),
so wiring the config in without anything calling it yet is inert by design, exactly like it is
today with Vault unconfigured.

**Manual verification** (part of this prompt's own "done," same standard as the July 26 prompt's
§9):
1. `kubectl get statefulset,pvc -n pena-e-arte` shows `vault-0` and its PVC bound, pod
   `Running` (correctly `0/1 Ready` — sealed — until §3.3's runbook runs).
2. After Phi runs §3.3's runbook by hand: `kubectl exec vault-0 -n pena-e-arte -- vault status`
   shows `Sealed: false`.
3. Open a real (draft is fine) PR touching `k8s/**`/`cd.yml`, confirm `ci.yml` still passes
   unaffected, then watch `cd.yml` run end-to-end once GHCR permissions (§1 step 5) and the
   §5 secrets are in place: image push visible in GHCR, migration Job completes, both
   Deployments roll out.
4. Load the deployed frontend at `https://app.tattooos.co`, open browser devtools, confirm the
   Stripe Elements / Google / Apple SDK calls are using real (not `placeholder`/`pk_test_`)
   client IDs and keys — the concrete check that §4's bug is actually fixed, not just that the
   workflow YAML looks right.

---

## 8. Docs to update (in addition to the July 26 prompt's own §12 list)

- `docs/infra/ADR-0002-secrets-management.md` — new dated "Addendum" section, exact text in §2
  above.
- `docs/claude/architecture.md` Decisions Log — amend the existing "Per-tenant secrets:
  ISecretsProvider..." row's Choice cell, appending (not replacing) the 1 Aug 2026 HCP Vault
  sentence with the 3 Sep 2026 reversal, same text as §2 above, condensed to fit the table's
  existing style.
- New `docs/infra/vault-self-hosted-runbook.md` — §3.3's runbook, written but not executed by
  this session.
- Final commit list, in order:
  1. `feat(k8s): add self-hosted single-node Vault (StatefulSet, raft storage) to the cluster`
  2. `feat(k8s): wire Vault:Address/Vault:Token into pena-e-arte-api-secrets`
  3. `fix(ci): cd.yml build-and-push uses real production VITE_* build-args, not CI placeholders`
  4. `docs(infra): ADR-0002 addendum — self-hosted Vault, not HCP Vault Dedicated (cost)`
  5. `docs(infra): add vault-self-hosted-runbook.md`
  6. `docs(architecture): log secrets-backend reversal in Decisions Log`

---

## 9. Final self-check

- [ ] Every item in §5's table is either confirmed present as a GitHub Actions secret, or
      explicitly called out in the final summary as still missing and blocking (with which
      exact phase it blocks).
- [ ] `vault operator init`/`unseal`/`login`/token-creation commands were **written into the
      runbook, never executed by this session**.
- [ ] No real secret value (Vault token, unseal key, root token, Stripe key, etc.) appears
      anywhere in this session's commits, docs, or output.
- [ ] `cd.yml`'s frontend build-args reference GitHub secrets/vars, not literal placeholder
      strings — grep the diff to confirm `pk_test_placeholder` does not appear in `cd.yml`.
- [ ] ADR-0002 and the Decisions Log both reflect the reversal with today's date and the actual
      HCP Vault Dedicated pricing that motivated it.
- [ ] Final summary states plainly which BLOCKING-MANUAL items (§1 step 5, §5's still-missing
      secrets, §3.3's init/unseal runbook) remain for Phi to do before `cd.yml` can run
      end-to-end successfully — this prompt does not claim production deploy is live at the end
      of this session; it claims the pipeline and manifests are ready for it.
