# Self-hosted Vault — init/unseal runbook (founder action, never automated)

**Status:** written but not executed by any Claude Code session · **Date:** 3 Sep 2026
**Related:** `docs/infra/ADR-0002-secrets-management.md` (3 Sep 2026 addendum),
`k8s/base/vault-statefulset.yaml`

---

`vault operator init` is destructive to get wrong: it generates the root token and unseal key
shares exactly once, and losing them means the data is unrecoverable short of wiping and
reinitializing. **No Claude Code session should ever run the commands below, see their output,
or be asked to relay the values anywhere** — this is the same "never paste a real secret into a
chat/doc" rule this project applies to every other production credential, applied here to key
material that's even more sensitive than an API key.

Run these directly against the cluster, by hand, after `k8s/base/vault-statefulset.yaml` is
deployed and the `vault-0` pod is `Running` (but `0/1 Ready` — sealed — which is expected):

```bash
kubectl exec -it tattooos-vault-0 -n tattooos -- vault operator init -key-shares=5 -key-threshold=3
```

Prints 5 unseal key shares and a root token. Record all 6 values somewhere durable and **never**
in git, chat, or any doc in this repo — a password manager or a printed-and-locked-away copy,
same standard as any other production root credential.

**Unseal** (needs 3 of the 5 key shares — run this command 3 times with 3 different shares):

```bash
kubectl exec -it tattooos-vault-0 -n tattooos -- vault operator unseal
```

**Authenticate with the root token, then create a scoped, non-root token for the app to use —
never put the root token itself into a GitHub secret:**

```bash
kubectl exec -it tattooos-vault-0 -n tattooos -- vault login <root-token>
kubectl exec -it tattooos-vault-0 -n tattooos -- vault secrets enable -path=secret kv-v2
kubectl exec -i tattooos-vault-0 -n tattooos -- vault policy write tattooos-app - <<'EOF'
path "secret/data/studios/*" {
  capabilities = ["create", "read", "update", "patch"]
}
EOF
kubectl exec -it tattooos-vault-0 -n tattooos -- vault token create -policy=tattooos-app -period=720h
```

Record the resulting token — this is the value for the `VAULT_TOKEN` GitHub Actions secret.
`-period=720h` makes it a renewable periodic token (30 days) rather than one with a hard expiry.
It expires if nobody renews it within 30 days: `VaultHealthCheckJob` renews it once less than half
of the period is left (see "How the alert works" below).

**Why these capabilities (2026-10-21):** the first version of this policy was `read` on
`secret/data/*`, but connecting POK *writes* the studio's keys to `secret/data/studios/{studioId}/pok`
(`ConnectPokAccountCommand`: KV v2 patch, or a plain create on a studio's first connect) and every
payment reads them. Checked against a dev Vault 1.18.5: with exactly these four capabilities a create,
a patch and a read on that path all succeed. Nothing else in the app touches Vault.

**GitHub Actions secrets to add from this runbook:**

```
VAULT_ADDR  = http://tattooos-vault.tattooos.svc.cluster.local:8200
VAULT_TOKEN = <the scoped token from `vault token create` above, NOT the root token>
```

## After every Vault pod restart

Vault re-seals on every restart (crash, node reboot, manual redeploy of the StatefulSet) — this
is a single-node, manually-unsealed deployment with no cloud-KMS auto-unseal (see ADR-0002's 3
Sep 2026 addendum for why). Repeat the `vault operator unseal` step above (3 of the 5 key
shares) before Vault is usable again. Every `ISecretsProvider` call fails closed until this is
done.

**What a sealed Vault breaks now** (POK card deposits read each studio's keys from Vault on every
payment operation): new card deposits, captures, refunds and releases fail with **503 "Card
payments are temporarily unavailable"**, and connecting POK fails with a 503 that says nothing was
changed. Cash, bookings and everything else keep working. No stored key is lost — the data is in
Raft storage and comes back the moment Vault is unsealed. Owners are **not** told to reconnect
(go-live blocker C-8; before that fix a sealed Vault read as "reconnect POK").

### When the "Vault sealed or unreachable" alert fires (ops@ email)

Chosen approach (ADR-0002, C-8): **manual unseal + alert**, no cloud-KMS auto-unseal.

1. Check the state: `kubectl exec -n tattooos tattooos-vault-0 -- vault status` — look at
   `Sealed`. If the pod isn't running, `kubectl get pod -n tattooos tattooos-vault-0` and fix that
   first.
2. If sealed: run `vault operator unseal` three times with three of the five key shares (the step
   above). Key shares never go into chat, tickets or this repo.
3. Confirm: `vault status` shows `Sealed false`. Within 5 minutes `VaultHealthCheckJob` stops logging
   `VaultHealthCheckFailed` and the alert resolves on its own (15-minute window).
4. If it was unreachable rather than sealed (`unreachable (...)` in the log line): check the pod,
   its PVC and `kubectl logs -n tattooos tattooos-vault-0`.

How the alert works: `VaultHealthCheckJob` (Hangfire, every 5 minutes) calls the unauthenticated
`/v1/sys/health` and logs `VaultHealthCheckFailed: Vault is {sealed|not initialised|unhealthy|
unreachable}` on anything but healthy. When Vault is healthy and the app has a token, it then checks
the token itself (since 2026-10-21 — the production token had silently expired around 7 Oct while
`/sys/health` still said healthy):
- `token/lookup-self`: 403 → `Vault is rejecting the app's token (invalid or expired)`. Fix: see
  "Replace the app's token" below.
- Under half of the token's period left → `token/renew-self`, logged as `VaultTokenRenewed` (info).
  A refused renewal → `Vault is refusing to renew the app's token`.
- `sys/capabilities-self` on a studio's POK-key path (nothing is written) → `Vault is missing
  permissions on the studios' POK keys (...)` lists what the policy lacks. Fix: re-run the
  `vault policy write tattooos-app` step above; the existing token picks the new policy up at once.

Real payment requests that hit a dead Vault log `Secrets store unavailable`. The Grafana rule `vault-unavailable` (`k8s/observability/grafana-alerting-
configmap.yaml`) emails ops@ when either line appears in production in the last 15 minutes.

Auto-unseal (a cloud KMS, or a second Vault's Transit engine) would remove the manual step but adds
cost and an external dependency; revisit if restarts become frequent.

## Replace the app's token (expired, revoked or leaked)

Founder action — it needs the root token. Vault must be unsealed first.

1. Log in with the root token (`vault login` without an argument prompts for it, hidden) and, if the
   policy is still the old read-only one, re-run the `vault policy write tattooos-app` step above.
2. `vault token create -policy=tattooos-app -period=720h` and copy the `token` value.
3. Remove the root token the login saved in the pod: `kubectl exec -n tattooos tattooos-vault-0 -- rm -f /root/.vault-token`
   (`kubectl exec` runs as root with `HOME=/root`; checked 2026-10-21).
4. `gh secret set VAULT_TOKEN` (it prompts for the value — never put it on the command line).
5. The token reaches the pods through `tattooos-api-secrets`, which CD rebuilds from GitHub secrets
   on every run, and the pods read it at start. So: the next deploy picks it up by itself; to apply it
   now, re-run the latest CD run, then `kubectl rollout restart deployment/tattooos-api -n tattooos`.
6. Within 5 minutes `VaultHealthCheckFailed` stops; a token that leaked is revoked with
   `vault token revoke -accessor <accessor>` (the accessor is in `vault list auth/token/accessors`).
