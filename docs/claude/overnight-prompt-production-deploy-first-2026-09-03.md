# Overnight Prompt — First Production Deploy (Tier 1, completion)

> Feed this file directly to Claude Code (running in the main **Pena e Artë - Engineering**
> project, with full repo write access) as the task prompt, **alongside**
> `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` and
> `docs/claude/overnight-prompt-cd-k8s-vault-2026-09-03.md`. Read all three files in full before
> writing anything. **This file does not repeat either of them — it sequences and completes
> them.** Both prior prompts already specify real, correct work (K8s manifests, `cd.yml`,
> in-cluster Vault) that has been built and merged but **never actually run against the live
> cluster**. This prompt's job is narrower and more operational than either: confirm today's
> real cluster state, gate on the real remaining BLOCKING-MANUAL items, and — once they clear —
> actually trigger the first production deploy and verify it end-to-end. **Mode: fully
> autonomous for everything that doesn't require a human with real credentials or root Vault
> key material.** Every BLOCKING-MANUAL item below must already be done before the phase that
> depends on it runs. If one is missing, stop, do everything else that doesn't depend on it, and
> report exactly what's blocking and why — do not improvise around a missing prerequisite (same
> standard both prior prompts already hold themselves to).

**Date logged:** 2026-09-03
**Requested by:** Phi
**Origin:** Engineering-consultation gap audit, verified today against the live repo, live
GitHub config, and a live `kubectl` session against the Hetzner cluster. Finding: the cluster
has **only stock K3s system pods** — no `cert-manager` CRDs, no `pena-e-arte` namespace,
nothing deployed. Every piece of work both prior prompts describe is real and merged; none of
it has been applied yet. This is the single highest-priority gap in the whole audit — nothing
in Tier 2 (staging) or the rest of the platform's production readiness can be verified
end-to-end until this lands.

**Checkpoint before starting:**
```bash
git status                     # must be clean before starting
git checkout main && git pull
git checkout -b feat/first-production-deploy
git commit --allow-empty -m "checkpoint: before first production deploy"
```

---

## 0. Re-verify today's state before doing anything — don't trust this doc blindly

State can move fast in this project (three overnight prompts landed today alone). Before
running any step below, confirm for yourself:

```bash
kubectl get ns                                    # expect: no pena-e-arte, no monitoring
kubectl get crd | grep cert-manager                # expect: empty
kubectl get pods -A                                # expect: only kube-system / stock K3s pods
gh secret list --repo 471k/pena-e-arte             # cross-check against §1 below
gh api repos/471k/pena-e-arte/actions/permissions/workflow   # workflow_permissions.default == "write"
```

If any of these disagree with what's written below (e.g. a namespace already exists, or more
secrets are already populated), **trust the live state, not this file** — adjust which phases
below still need doing, and say so plainly in the final summary.

**Update, found after this file was first written (same day):** a session merged the
staging-environment PR and watched the `cd.yml` run it auto-triggered rather than assuming
success. Result: `deploy` has never once gotten past its first `kubectl` call, across all four
CD runs attempted today — every one fails identically with
`dial tcp 49.13.66.15:6443: i/o timeout`. **No migration has run. Production has never actually
been touched by CD.** Near-certain root cause: the Hetzner Cloud Firewall (`pena-e-arte-k3s-fw`)
restricts `6443/tcp` to the operator's own IP only (by design, per the July 26 prompt's own
Phase 0). GitHub-hosted Actions runners use ephemeral IPs nowhere on that allowlist — the
`KUBE_CONFIG` secret is valid (confirmed working from the operator's own machine), but the TCP
connection itself is dropped before authentication is ever attempted. **This means Phase 3 below
(triggering `cd.yml`'s `deploy` job) cannot succeed as written, regardless of how complete §1's
secrets gate is** — fix this first, or route around it, before spending effort on anything else
in this file that assumes `cd.yml` can actually reach the cluster. Three options, all requiring
a real decision from Phi (do not silently pick one):
1. **Self-hosted GitHub Actions runner** on the Hetzner box itself (or another host already
   inside the firewall's allowed access) — most secure, `6443` never needs to be
   internet-facing; adds an always-on runner process.
2. **Dynamic IP allowlisting via the Hetzner Cloud API** as steps at the start/end of `deploy`
   (add the runner's current IP to `pena-e-arte-k3s-fw`, run kubectl, remove it) — keeps
   GitHub-hosted runners, needs a scoped Hetzner API token as a new secret plus real
   firewall-management logic added to `cd.yml`.
3. **Open `6443/tcp` to GitHub Actions' published IP ranges** (`api.github.com/meta`) —
   simplest, but those ranges are broad and change over time, a real weakening from Phase 0's
   original "never expose this world-wide" reasoning.

**A separate, immediately-available option for THIS FIRST deploy specifically** (distinct from
fixing CD long-term): a Claude Code session's own `kubectl`, run locally against the operator's
already-working kubeconfig, is not behind this firewall restriction at all — confirmed working
in this exact session. A one-time **manual** first deploy (apply the production overlay
directly via local `kubectl`, bypassing `cd.yml` entirely for this one run) could unblock
Tier 1 today without resolving the runner-connectivity problem first — but this is a
meaningfully more consequential action than anything else in this file (a real, first-ever
migration against the live production database, real manifests applied to the live cluster) and
**must not be done without Phi's explicit, specific go-ahead for that exact action** — do not
infer it from this file's general "fully autonomous" framing. If Phi wants this path, say so
plainly; otherwise treat options 1-3 above as this file's actual Phase 3 blocker.

---

## 1. BLOCKING-MANUAL gate — confirm before Phase 3 (the real `kubectl apply`)

These are real external credentials. Per both prior prompts' standing rule: **never paste a
real secret value into a chat, prompt, or doc anywhere — add them directly in the GitHub
Settings UI.** This session only checks presence (`gh secret list` shows names, never values),
never values.

| Secret | Required for | If missing |
|---|---|---|
| `VITE_GOOGLE_CLIENT_ID` | frontend build (`cd.yml`), Google sign-in | Do Phase 1 (cert-manager) and Phase 2 (manifest dry-run/lint) regardless. **Do not trigger the real `cd.yml` deploy (Phase 3) until this exists** — without it, `cd.yml`'s corrected build-args (per the `cd-k8s-vault` prompt's §4 fix) substitute an empty string, silently shipping a build with Google sign-in broken, exactly the kind of silent-failure gap this project's rules say to catch, not reproduce. |
| `VITE_APPLE_CLIENT_ID` | frontend build (`cd.yml`), Apple sign-in | Same as above, for Apple sign-in. |
| GHCR `packages: write` | `cd.yml`'s `build-and-push` job | Confirmed enabled today (`gh api .../actions/permissions/workflow` → `"default_workflow_permissions":"write"`) — re-verify with the command in §0; if it somehow regressed, stop and flag rather than pasting a PAT into the workflow file. |

**Not required to unblock this prompt's own scope** (explicitly deferred, not an oversight):
- `VAULT_TOKEN` — only producible after Phase 3 stands the `vault-0` pod up (§4 below). The app
  doesn't call `ISecretsProvider` in any live path yet (confirmed in both prior prompts), so its
  absence at first deploy is inert — Phase 3 proceeds without it, and `pena-e-arte-api-secrets`
  simply gets an empty `Vault__Token` value until §4 is done and `cd.yml` is re-run once more.
- `STRIPE_SECRET_KEY` / `STRIPE_PUBLISHABLE_KEY` / `STRIPE_WEBHOOK_SECRET_BILLING` /
  `STRIPE_WEBHOOK_SECRET_CONNECT` — **do not treat this as a task to complete.** Per project
  memory, Stripe is unavailable at the country level for this platform's Albania-registered
  legal entity; populating these with live keys isn't currently possible and is an external/
  business decision (Tier 6/7 of the source audit), not something this session resolves. Leave
  them unset — `NullPaymentProvider` already fails closed by design (confirmed in
  `Pena_e_Arte.Infrastructure/Services/NullPaymentProvider.cs`), so the app starts and runs
  fine with card payments simply unavailable. Do not attempt to wire a substitute provider here
  — that's Tier 3 territory (a separate prompt) and a real business decision, not an infra gap.
- Every optional/config-gated secret already enumerated in the `cd-k8s-vault` prompt's §5 table
  (Instagram/TikTok/Facebook/X/YouTube) — confirmed there as safe to leave empty at launch.

If `VITE_GOOGLE_CLIENT_ID`/`VITE_APPLE_CLIENT_ID` are missing when you reach Phase 3: **do Phases
1 and 2 anyway** (cert-manager install and manifest validation are independently useful and
unblock nothing else), then stop before triggering the real deploy and report precisely that
these two secrets are the only thing standing between "ready" and "live."

---

## 2. Phase 1 — Install cert-manager (cluster-level one-time bootstrap)

Not part of `cd.yml`'s own steps — this is a manual, one-time cluster bootstrap per both prior
prompts' own design (a CD pipeline should not have the permissions to install cluster-scoped
CRDs on every run).

```bash
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/<pinned-tag>/cert-manager.yaml
```

Resolve `<pinned-tag>` to the exact current stable release tag from cert-manager's GitHub
releases page before running this — **do not use `latest`** in the URL or in any manifest.
State the exact tag used in the final summary so it's on record.

Verify before proceeding to anything that references `ClusterIssuer`/`Certificate`:
```bash
kubectl get pods -n cert-manager
# cert-manager, cert-manager-cainjector, cert-manager-webhook all Running
```

---

## 3. Phase 2 — Manifest validation (safe to do regardless of §1's gate)

Before the real apply, dry-run everything that's been merged but never applied:

```bash
kubectl apply --dry-run=server -k k8s/base/
kubectl apply --dry-run=server -k k8s/overlays/production/   # or whichever overlay is production's
```

Fix any schema errors this surfaces (a manifest that's never been applied to a real API server
can have drifted from what the installed CRD versions actually accept — e.g. cert-manager's own
`ClusterIssuer`/`Certificate` CRD versions). This is exactly the kind of gap a dry-run catches
before it costs a failed rollout. If nothing surfaces, say so in the final summary rather than
silently skipping this step.

---

## 4. Phase 3 — Trigger the real deploy (gated on §1)

Once §1's two `VITE_*` secrets are confirmed present:

1. Trigger `cd.yml`'s `deploy` job — either merge a PR into `main` that causes `ci.yml` to pass
   (the `workflow_run` trigger both prior prompts specify), or use `gh workflow run cd.yml` /
   the Actions UI's manual dispatch if `cd.yml` supports `workflow_dispatch`. Confirm which
   trigger mode is actually wired before assuming either works — read `cd.yml` itself rather
   than guessing.
2. Watch the run: `gh run watch` or `gh run view --log` on the specific run ID. If
   `build-and-push` fails on a GHCR permissions error, stop — do not paste a PAT into the
   workflow as a workaround (§1's own rule).
3. Once the workflow completes, verify against the live cluster:
   ```bash
   kubectl get pods -n pena-e-arte
   # api, frontend, redis, vault-0 all present; vault-0 is Running but 0/1 Ready (sealed —
   # correct, expected, see §5 below)
   kubectl rollout status deployment/pena-e-arte-api -n pena-e-arte
   kubectl rollout status deployment/pena-e-arte-frontend -n pena-e-arte
   ```
4. TLS — confirm a real Let's Encrypt cert, not self-signed or still-pending:
   ```bash
   curl -I https://app.tattooos.co
   openssl s_client -connect app.tattooos.co:443 -servername app.tattooos.co </dev/null 2>/dev/null \
     | openssl x509 -noout -issuer
   ```
   The issuer line should show Let's Encrypt, not a self-signed/temporary cert-manager
   bootstrap certificate. If it's still self-signed after a few minutes, check
   `kubectl describe certificate -n pena-e-arte` and `kubectl describe challenge -n pena-e-arte`
   for ACME/DNS-01 propagation issues before assuming something is broken.
5. Observability — same pattern for the `monitoring` namespace:
   ```bash
   kubectl get pods -n monitoring
   # Prometheus/Loki/Tempo/Alloy/Grafana all Running
   ```
6. Concrete runtime check, not just "the workflow was green": load
   `https://app.tattooos.co` in a real browser or via `curl`, open devtools network tab (or
   fetch the bundled JS and grep it) and confirm the Google/Apple client IDs baked into the
   frontend bundle are the real values, not empty strings or the CI's
   `placeholder`/`pk_test_placeholder` tokens — this is the concrete check the `cd-k8s-vault`
   prompt's §4 fix was for; confirm the fix is actually working end-to-end, not just that the
   YAML looks right.

---

## 5. Phase 4 — Vault init/unseal — BLOCKING-MANUAL, Phi only, hand off, do not execute

`docs/infra/vault-self-hosted-runbook.md` already exists (written by the `cd-k8s-vault` prompt)
and has never been run. It can only run **after** Phase 3 above stands up the `vault-0` pod.
**This session must not run `vault operator init`, `unseal`, or `login`, must not see their
output, and must not be asked to relay the resulting key shares or root token anywhere** — the
runbook is explicit about this and that rule does not change here. This session's only job
regarding Phase 4 is:
1. Confirm `vault-0` is `Running` (0/1 Ready — sealed is the correct, expected state) after
   Phase 3.
2. State plainly in the final summary that `docs/infra/vault-self-hosted-runbook.md` is ready
   to run and is the one remaining manual step, with the exact current pod status as evidence
   it's unblocked.
3. Do not touch `VAULT_TOKEN` in GitHub Secrets — that's produced by Phi running the runbook,
   not by this session.

---

## 6. Phase 5 — Capacity check (gates Tier 2 staging work, not this prompt)

Once Phases 1–3 are done and production is `Running`:
```bash
kubectl describe nodes | grep -A 5 "Allocated resources"
kubectl top nodes
```
Record the actual numbers in the final summary. Needs roughly ≥700Mi allocatable memory
headroom remaining (Hetzner CPX22, 2 vCPU/4GB) before a staging overlay could be applied on the
same box — **do not apply anything staging-related in this prompt regardless of the result**;
that's the next prompt's job (`docs/claude/overnight-prompt-staging-goes-live-2026-09-03.md`).
This step exists purely so that prompt doesn't have to re-derive live capacity numbers from
scratch. If headroom is short, say so and point at `docs/infra/staging-environment.md`'s
priced fallback (a second small Hetzner box) rather than proposing to shrink production's own
resource requests to make room.

---

## 7. Explicitly out of scope

- Anything staging-related (Tier 2) — separate prompt, sequenced after this one.
- Wiring a real payment provider, Stripe or otherwise (Tier 6/7 — external business decision).
- Vault auto-unseal, HA, or any code change to route app secrets through `ISecretsProvider` —
  already named out of scope by the `cd-k8s-vault` prompt's own §6 and unchanged here.
- Alerting, status page, external uptime monitoring, backup/DR runbook — Tier 4, separate
  prompt, mostly gated on this one landing first.
- Running the Vault init/unseal runbook yourself — §5 above.

---

## 8. Final self-check

- [ ] §0's live-state check was actually run and its output is quoted in the final summary,
      not assumed from this file.
- [ ] cert-manager is installed at a pinned, named release tag (not `latest`) and its three
      pods are confirmed `Running` before anything referencing `ClusterIssuer`/`Certificate`
      was applied.
- [ ] `kubectl apply --dry-run=server` was run against both `k8s/base/` and the production
      overlay before the real apply, and any errors it surfaced were fixed, not ignored.
- [ ] If `VITE_GOOGLE_CLIENT_ID`/`VITE_APPLE_CLIENT_ID` were missing, this session did
      everything else in this prompt that doesn't depend on them and stopped cleanly before
      Phase 3, with a precise statement of what's still blocking.
- [ ] If both were present: `cd.yml` ran to completion, all pods in `pena-e-arte` and
      `monitoring` are `Running`/rolled out, the TLS cert is real (Let's Encrypt, not
      self-signed), and the frontend bundle was actually checked (not assumed) to contain real
      OAuth client IDs.
- [ ] `vault operator init`/`unseal`/`login` were **not run by this session under any
      circumstance**, regardless of how ready `vault-0` looks.
- [ ] The capacity numbers in §6 are recorded in the final summary for the next prompt to use.
- [ ] The final summary states plainly whether production is actually live end-to-end right
      now, or exactly which BLOCKING-MANUAL item(s) are the only thing stopping it — this
      prompt does not claim success it hasn't verified against the real cluster.
