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
kubectl exec -it pena-e-arte-vault-0 -n pena-e-arte -- vault operator init -key-shares=5 -key-threshold=3
```

Prints 5 unseal key shares and a root token. Record all 6 values somewhere durable and **never**
in git, chat, or any doc in this repo — a password manager or a printed-and-locked-away copy,
same standard as any other production root credential.

**Unseal** (needs 3 of the 5 key shares — run this command 3 times with 3 different shares):

```bash
kubectl exec -it pena-e-arte-vault-0 -n pena-e-arte -- vault operator unseal
```

**Authenticate with the root token, then create a scoped, non-root token for the app to use —
never put the root token itself into a GitHub secret:**

```bash
kubectl exec -it pena-e-arte-vault-0 -n pena-e-arte -- vault login <root-token>
kubectl exec -it pena-e-arte-vault-0 -n pena-e-arte -- vault secrets enable -path=secret kv-v2
kubectl exec -it pena-e-arte-vault-0 -n pena-e-arte -- vault policy write pena-e-arte-app - <<'EOF'
path "secret/data/*" {
  capabilities = ["read"]
}
EOF
kubectl exec -it pena-e-arte-vault-0 -n pena-e-arte -- vault token create -policy=pena-e-arte-app -period=720h
```

Record the resulting token — this is the value for the `VAULT_TOKEN` GitHub Actions secret.
`-period=720h` makes it a renewable periodic token (30 days) rather than one with a hard expiry;
nothing currently renews it automatically — a follow-up, not solved here.

**GitHub Actions secrets to add from this runbook:**

```
VAULT_ADDR  = http://pena-e-arte-vault.pena-e-arte.svc.cluster.local:8200
VAULT_TOKEN = <the scoped token from `vault token create` above, NOT the root token>
```

## After every Vault pod restart

Vault re-seals on every restart (crash, node reboot, manual redeploy of the StatefulSet) — this
is a single-node, manually-unsealed deployment with no cloud-KMS auto-unseal (see ADR-0002's 3
Sep 2026 addendum for why). Repeat the `vault operator unseal` step above (3 of the 5 key
shares) before Vault is usable again. Every `ISecretsProvider` call fails closed until this is
done.

**Zero functional impact today** — nothing in the codebase calls `ISecretsProvider` in a live
request path yet, confirmed against `InfrastructureServiceExtensions.cs` — but this is a real
operational gap that must be resolved (auto-unseal via a cloud KMS, or a documented on-call
paging step) before the per-tenant-credentials feature this mechanism exists for (ADR-0001
Article 4(g), `StudioCredentialRef`) actually ships and starts depending on Vault being
reachable.
