# Self-hosted GitHub Actions runner on the production box (founder action, one-time)

**Status:** written but not executed by any Claude Code session · **Date:** 4 Sep 2026
**Related:** `.github/workflows/cd.yml` (`deploy`/`deploy-staging` jobs), `docs/claude/architecture.md`
Decisions Log

---

## Why this exists

Every CD run against production has failed identically since `cd.yml` was written:
`dial tcp 49.13.66.15:6443: i/o timeout` on the very first `kubectl` call. Root cause: the
Hetzner Cloud Firewall (`pena-e-arte-k3s-fw`) restricts `6443/tcp` to the operator's own IP only
(by design — see `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` §0 step 1).
GitHub-hosted runners use ephemeral IPs nowhere on that allowlist, so the TCP connection is
dropped before `KUBE_CONFIG` is ever used.

**Decision (4 Sep 2026):** run a self-hosted GitHub Actions runner directly on the
`pena-e-arte-k3s` box, so `deploy`/`deploy-staging` execute *on the cluster node itself* and
never cross the public network to reach 6443 at all. This was chosen over the two alternatives
considered:
- **Dynamic Hetzner-API IP allowlisting** — would have kept GitHub-hosted runners, at the cost of
  a new scoped Hetzner API token (another standing secret) and firewall-management logic added to
  `cd.yml` (with its own failure mode: a crashed step that never removes the allowed IP).
- **Opening 6443 to GitHub's published IP ranges** — simplest, but a real, permanent weakening of
  Phase 0's original "never expose this world-wide" stance, and those ranges change over time.

A self-hosted runner needs no new secret, keeps 6443 closed to the internet exactly as Phase 0
intended, and eliminates the failure mode entirely rather than working around it.

**This repo is public** — self-hosted runners are a known risk there, because a workflow
triggered by a fork's `pull_request` can run arbitrary code on the runner before any review.
That risk does not apply to this runner: `cd.yml`'s `deploy`/`deploy-staging` jobs trigger only on
`workflow_run` (fires after `ci.yml` succeeds on `main` — i.e., only after code is already merged)
or `workflow_dispatch` (requires write access to trigger). **Never add a `pull_request` trigger,
or any trigger a non-collaborator can cause, to a job that runs on this runner's label** — that
is the one rule that keeps this safe on a public repo, and it must hold forever, not just today.

No Claude Code session can execute this runbook: it requires an interactive SSH session using the
passphrase-protected key at `C:\Users\User\.ssh\hetzner-pena-e-arte`, which — per this project's
standing rule — was never recorded anywhere a session could read it.

---

## 1. Create a dedicated, low-privilege runner user

Do not run the runner as `root`. It will execute CI-triggered `kubectl`/`kustomize`/`yq` commands
with whatever permissions this user has — scope it to exactly that, nothing more.

```bash
ssh -i ~/.ssh/hetzner-pena-e-arte root@49.13.66.15
sudo useradd -m -s /bin/bash github-runner
```

## 2. Give that user a working kubeconfig — via loopback, not the public IP

The whole point of this runner is to never cross the public firewall. K3s's own
`/etc/rancher/k3s/k3s.yaml` already points at `https://127.0.0.1:6443` with valid embedded certs
— reuse it as-is rather than the externally-rewritten copy used for the `KUBE_CONFIG` GitHub
secret.

```bash
sudo mkdir -p /home/github-runner/.kube
sudo cp /etc/rancher/k3s/k3s.yaml /home/github-runner/.kube/config
# Confirm it still says 127.0.0.1, not the public IP — if someone edited it in place, fix it:
sudo sed -i 's#server: https://.*:6443#server: https://127.0.0.1:6443#' /home/github-runner/.kube/config
sudo chown -R github-runner:github-runner /home/github-runner/.kube
sudo chmod 700 /home/github-runner/.kube
sudo chmod 600 /home/github-runner/.kube/config
```

With this in place, `cd.yml`'s `deploy`/`deploy-staging` jobs no longer need a "write kubeconfig
from `KUBE_CONFIG` secret" step when running on this label — `kubectl` picks up
`~/.kube/config` by default for whichever user the runner service runs as. Keep the `KUBE_CONFIG`
GitHub secret around regardless — it's still what the operator's own local `kubectl` uses.

## 3. Install kubectl, kustomize, yq (pinned, not `latest`)

GitHub-hosted `ubuntu-latest` runners ship these preinstalled; a bare Ubuntu box does not.
Versions below were the current stable releases as of 4 Sep 2026 — matched to the cluster's own
`v1.36.2+k3s1` server version for kubectl (client/server skew policy allows ±1 minor; matching the
minor exactly is the safer default). Confirm these are still the versions you want before running
(check `https://dl.k8s.io/release/stable-1.36.txt`, the `kubernetes-sigs/kustomize` and
`mikefarah/yq` GitHub releases pages) — don't blindly trust a doc written weeks before you run it.

```bash
# kubectl v1.36.4
curl -fsSLo /usr/local/bin/kubectl "https://dl.k8s.io/release/v1.36.4/bin/linux/amd64/kubectl"
sudo install -o root -g root -m 0755 /usr/local/bin/kubectl /usr/local/bin/kubectl

# kustomize v5.8.1
curl -fsSL "https://github.com/kubernetes-sigs/kustomize/releases/download/kustomize%2Fv5.8.1/kustomize_v5.8.1_linux_amd64.tar.gz" \
  | sudo tar -xz -C /usr/local/bin kustomize

# yq v4.53.6
curl -fsSLo /tmp/yq "https://github.com/mikefarah/yq/releases/download/v4.53.6/yq_linux_amd64"
sudo install -o root -g root -m 0755 /tmp/yq /usr/local/bin/yq

kubectl version --client
kustomize version
yq --version
```

## 4. Register the runner with GitHub

Go to `https://github.com/471k/pena-e-arte/settings/actions/runners/new` (Linux, x64) — GitHub
shows you the exact download+config commands with a fresh registration token embedded (valid
~1 hour). **Run those commands yourself, directly on the box, as the `github-runner` user — do
not paste the registration token anywhere else, including back into a chat with Claude.**

```bash
sudo -iu github-runner
mkdir actions-runner && cd actions-runner
curl -fsSLo actions-runner-linux-x64.tar.gz \
  "https://github.com/actions/runner/releases/download/v2.337.0/actions-runner-linux-x64-2.337.0.tar.gz"
tar xzf actions-runner-linux-x64.tar.gz

./config.sh --url https://github.com/471k/pena-e-arte \
  --token <PASTE THE TOKEN GITHUB JUST SHOWED YOU> \
  --name pena-e-arte-hetzner \
  --labels pena-e-arte-prod \
  --work _work \
  --unattended
```

(Confirm `v2.337.0` is still current on `https://github.com/actions/runner/releases` before
using it — same "don't trust a pinned version in an old doc" caution as step 3.)

## 5. Install it as a systemd service (survives reboots, auto-restarts on crash)

Back as a `sudo`-capable user (exit the `github-runner` shell first):

```bash
exit   # back to your own sudo-capable session
cd /home/github-runner/actions-runner
sudo ./svc.sh install github-runner
sudo ./svc.sh start
sudo ./svc.sh status
```

## 6. Verify

- `https://github.com/471k/pena-e-arte/settings/actions/runners` shows `pena-e-arte-hetzner`,
  label `pena-e-arte-prod`, status **Idle**.
- `sudo -u github-runner kubectl get ns` on the box succeeds without a `KUBECONFIG` env var set
  (proves the loopback kubeconfig from step 2 works for that user).
- `kubectl top nodes` (from your own machine, or as `github-runner`) — confirm the idle runner's
  own memory footprint (typically ~100–150Mi for `Runner.Listener`) still leaves comfortable
  headroom on the CPX22's 4GB alongside production + observability + Vault. Record the number
  wherever the next capacity check (staging-overlay gate) reads it from.

Once this shows Idle, `cd.yml`'s `deploy`/`deploy-staging` jobs (already wired to
`runs-on: [self-hosted, pena-e-arte-prod]`) will pick up the next `workflow_run`/
`workflow_dispatch` trigger automatically — no further repo change needed.

## Ongoing maintenance (accepted, named, not automated)

- **Runner + OS updates are manual.** Nothing patches this box's runner binary or OS packages
  automatically. Check `https://github.com/471k/pena-e-arte/settings/actions/runners` periodically
  for a "runner needs updating" flag; `sudo ./svc.sh stop && ./config.sh remove` + re-run steps
  4–5 with a fresh tarball if a major version bump is ever needed.
- **No automatic fallback if this runner is offline.** A `deploy`/`deploy-staging` run targeting
  `[self-hosted, pena-e-arte-prod]` simply queues indefinitely if the service isn't running —
  GitHub does not fall back to a hosted runner. If the box reboots, `systemd` should bring the
  service back on its own (confirm with `sudo ./svc.sh status` after any reboot); if it doesn't,
  CD silently stalls rather than failing loudly — worth a follow-up health check/alert, not solved
  here.
