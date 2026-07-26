# Overnight Prompt — Stand Up CI: GitHub Actions Pipeline (Backend + Frontend + Guardrails + Security)

**Scope:** There is currently no `.github/` directory in this repo at all — no CI runs on push
or PR despite GitHub Actions being the documented CI/CD tool in `CLAUDE.md`. Correctness today
depends on someone remembering to run `dotnet test` and `pnpm test`/`pnpm lint` locally before
pushing. This prompt builds the missing pipeline: automated build+test+lint+security gates on
every push and PR, wired so nothing merges to `main` without passing. No deployment/CD (K3s
rollout) is in scope here — that is a separate, larger initiative that needs registry and
cluster secrets this prompt does not provision. Flag it as a follow-up at the end; do not build it now.

---

## Read First

1. `CLAUDE.md` — non-negotiable rules 1–8, especially #4 (secrets), #5 (structured logs),
   #6 (no `any`/`var`, tests required), #7 (industry-standard bar)
2. `docs/claude/backend.md`
3. `docs/claude/conventions.md` — git conventions, branch naming, testing conventions
4. `docs/claude/architecture.md` — Decisions Log format (you'll add an entry there)
5. `docker-compose.yml` and `.env.example` — ground truth for service versions/credentials
6. `Pena_e_Arte.API/Dockerfile` and `frontend/Dockerfile` — ground truth for exact SDK/Node/pnpm versions
7. `tests/Pena_e_Arte.IntegrationTests/Infrastructure/DatabaseFixture.cs` — ground truth for
   how integration tests connect to MySQL (this dictates the CI service container config —
   read it before writing the workflow, don't guess)
8. `frontend/playwright.config.ts` — already has `process.env.CI` branches; it was written
   expecting CI to exist one day, so honor its conventions rather than re-inventing them

---

## Working Context — Confirmed Facts (verified against the repo, not assumed)

- Repo: `github.com/471k/pena-e-arte`, default branch `main`. Branches in flight use both
  `feat/*` and `feature/*` prefixes — inconsistent, not your problem to fix here.
- Solution file is `Pena e Arte.slnx` (slnx format, **note the literal space in the filename**
  — always quote it: `"Pena e Arte.slnx"`). Contains 5 source projects + 2 test projects.
- Backend: .NET **10**, confirmed via `Pena_e_Arte.API/Dockerfile`
  (`mcr.microsoft.com/dotnet/sdk:10.0`). There is **no `global.json`** pinning the SDK version —
  you're adding one (Phase 1).
- `tests/Pena_e_Arte.UnitTests` — uses `Microsoft.EntityFrameworkCore.InMemory` and NSubstitute.
  **No external dependencies.** Needs nothing but the SDK to run.
- `tests/Pena_e_Arte.IntegrationTests` — uses `DatabaseFixture` which connects to
  `Server=127.0.0.1;Port=3306;...User=root;Password=root;AllowPublicKeyRetrieval=true;SslMode=None;`
  and creates/drops a uniquely-named database per fixture instance via `EnsureCreatedAsync`/
  `EnsureDeletedAsync`. **It expects a real MySQL server already listening on `127.0.0.1:3306`
  with `root`/`root`** — this is not Testcontainers-managed despite the
  `Testcontainers.MySql` package reference sitting unused in the `.csproj` (leave that alone,
  out of scope). All external services in these tests (Stripe, notifications, realtime, job
  scheduler) are `NSubstitute`-mocked at the handler level — **no Stripe/Resend/Twilio/Redis
  secrets are needed to run the test suite in CI.** Do not provision secrets you don't need.
- Frontend: Node **24** and pnpm **11.5.1**, confirmed via `frontend/Dockerfile`
  (`corepack prepare pnpm@11.5.1 --activate`). `frontend/package.json` scripts:
  `lint` (eslint), `build` (`tsc -b && vite build`), `test` (`vitest run`),
  `test:e2e` (`playwright test`).
- `frontend/playwright.config.ts` already detects `process.env.CI`: `forbidOnly`, retries=2,
  `reporter: "github"`, and does **not** need the .NET backend running — it mocks API calls
  via Playwright route interception. This makes e2e cheap to run in CI (no MySQL/API needed).
- Both `Pena_e_Arte.API/Dockerfile` and `frontend/Dockerfile` are real, working, multi-stage
  builds already used for `docker compose up --build`. Nothing currently verifies they still
  build on every push — that's a real gap given how often Dockerfile-adjacent files (`.csproj`,
  `package.json`) change.
- `.editorconfig` sets formatting rules (charset, indent, line endings) but no C# analyzer
  severities for the "no unclear `var`" rule from `CLAUDE.md` — don't assume `dotnet format`
  or `dotnet build` will catch that rule; it currently isn't enforced anywhere. You are not
  asked to fix that gap tonight (would require an `.editorconfig` analyzer-severity pass and
  likely surface pre-existing violations); just don't claim CI enforces it when it doesn't.

---

## Guiding principle for every gate you add

**A CI check that isn't required to merge doesn't enforce anything — it's decoration.**
Every job below must (a) actually fail the run on a real violation, and (b) be added to
branch protection as a required status check (Phase 8). Before wiring any *new* strict gate
(format-verify, lint, guardrail regexes) as blocking, run it once against the current `main`
branch state first. If it's already clean, wire it as blocking immediately. If it surfaces
pre-existing violations:
- **Small number (roughly ≤20, mechanical, low-risk to fix)** — fix them in this same change
  as a dedicated `chore: ` commit, *then* wire the gate as blocking. This is strongly preferred;
  a CI setup that ships with known-red checks trains people to ignore CI.
- **Large number, or fixes touch business logic you're not confident about** — wire the gate
  as non-blocking (`continue-on-error: true`, clearly commented `// TODO: remove
  continue-on-error once baseline is clean`), open a note in the fix-log described in Phase 7,
  and say so explicitly in your final summary. Do not silently leave any check unenforced
  without flagging it.

---

## Phase 1 — Pin the toolchain

Run `dotnet --version` on the machine you're working on and use the exact result (do not
guess a version number). Create `global.json` at the repo root:

```json
{
  "sdk": {
    "version": "10.0.1xx",
    "rollForward": "latestFeature"
  }
}
```

Replace `10.0.1xx` with the real installed version. This makes local dev, CI, and the
Dockerfile's `sdk:10.0` base image agree on a single major/feature band, and gives
`actions/setup-dotnet` a `global.json`-aware version to install in CI (see Phase 2 — use
`dotnet-version: |` reading from `global.json` rather than hardcoding the version a second
time in the workflow, so there's one source of truth).

---

## Phase 2 — Core CI workflow: `.github/workflows/ci.yml`

Triggers: every PR targeting `main`, every push to `main` (post-merge safety net), and
`workflow_dispatch` for manual re-runs. Add a concurrency group so superseded pushes to the
same PR cancel their in-flight run instead of queuing (saves Actions minutes, standard
practice):

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]
  workflow_dispatch: {}

concurrency:
  group: ci-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read

jobs:
  backend:
    name: Backend — build, format, test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Restore
        run: dotnet restore "Pena e Arte.slnx"

      - name: Format check (no changes allowed)
        run: dotnet format "Pena e Arte.slnx" --verify-no-changes --verbosity diagnostic
        # If Phase 6 found pre-existing violations too large to fix tonight, change this to:
        #   continue-on-error: true
        # and say so in the final summary — do not delete the step.

      - name: Build (Release)
        run: dotnet build "Pena e Arte.slnx" --configuration Release --no-restore

      - name: Start MySQL 8.4 (matches docker-compose.yml exactly)
        run: |
          docker run -d --name ci-mysql \
            -e MYSQL_ROOT_PASSWORD=root \
            -e MYSQL_DATABASE=pena_e_arte_test \
            -p 3306:3306 \
            mysql:8.4 \
            --character-set-server=utf8mb4 --collation-server=utf8mb4_unicode_ci
          echo "Waiting for MySQL to accept connections..."
          for i in $(seq 1 30); do
            if docker exec ci-mysql mysqladmin ping -uroot -proot --silent; then
              echo "MySQL is ready."
              break
            fi
            sleep 2
          done
          docker exec ci-mysql mysqladmin ping -uroot -proot --silent || {
            echo "MySQL did not become ready in time"; docker logs ci-mysql; exit 1;
          }
        # Deliberately a `docker run` step, not a GitHub Actions `services:` block — the
        # declarative `services:` syntax cannot pass the `--character-set-server` /
        # `--collation-server` startup flags docker-compose.yml uses, and DatabaseFixture.cs
        # connects to 127.0.0.1:3306 directly (not a service-network hostname), so a plain
        # `docker run` with -p 3306:3306 is the closer match to local dev. If integration
        # tests still fail on charset-sensitive assertions, this is the first place to check.

      - name: Unit tests
        run: >
          dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj
          --configuration Release --no-build
          --logger "trx;LogFileName=unit-tests.trx"
          --collect:"XPlat Code Coverage"

      - name: Integration tests
        run: >
          dotnet test tests/Pena_e_Arte.IntegrationTests/Pena_e_Arte.IntegrationTests.csproj
          --configuration Release --no-build
          --logger "trx;LogFileName=integration-tests.trx"
          --collect:"XPlat Code Coverage"
        # No Stripe/Resend/Twilio/Redis secrets are set here on purpose — every external
        # service is NSubstitute-mocked at the handler level (verified in
        # AppointmentHandlerIntegrationTests.cs). If a future integration test needs a real
        # secret, add it via repo Actions secrets at that time — don't pre-provision unused
        # secrets against the "secrets never in source, minimize blast radius" spirit of rule 4.

      - name: Stop MySQL
        if: always()
        run: docker rm -f ci-mysql || true

      - name: Publish test results
        uses: dorny/test-reporter@v1
        if: always()
        with:
          name: Backend test results
          path: "**/*.trx"
          reporter: dotnet-trx
          fail-on-error: true

      - name: Upload coverage artifact
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: backend-coverage
          path: "**/coverage.cobertura.xml"
          retention-days: 14

  frontend:
    name: Frontend — lint, typecheck, build, unit test, e2e
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: 24

      - name: Enable pnpm (pinned version — must match frontend/Dockerfile)
        run: corepack enable && corepack prepare pnpm@11.5.1 --activate

      - uses: actions/cache@v4
        with:
          path: ~/.local/share/pnpm/store
          key: pnpm-${{ runner.os }}-${{ hashFiles('frontend/pnpm-lock.yaml') }}
          restore-keys: pnpm-${{ runner.os }}-

      - name: Install (frozen lockfile)
        working-directory: frontend
        run: pnpm install --frozen-lockfile

      - name: Lint
        working-directory: frontend
        run: pnpm lint

      - name: Typecheck + build
        working-directory: frontend
        run: pnpm build
        # `pnpm build` runs `tsc -b && vite build` — this is both the TypeScript strict-mode
        # gate (rule 6: no `any`, explicit types) and a build-breakage gate in one step.

      - name: Unit / component tests
        working-directory: frontend
        run: pnpm test -- --coverage
        # If `vitest run` doesn't already have a coverage provider configured
        # (check frontend/vite.config.ts / vitest config), either add `@vitest/coverage-v8`
        # as a new devDependency (this is the one new-package exception worth taking — coverage
        # visibility directly serves "enforce correctness on every push") or drop `--coverage`
        # here rather than let the step fail on a misconfigured flag. Confirm which case you're
        # in before finalizing this step.

      - name: Install Playwright browsers
        working-directory: frontend
        run: pnpm exec playwright install --with-deps chromium

      - name: E2E tests
        working-directory: frontend
        run: pnpm test:e2e
        env:
          CI: true

      - name: Upload Playwright report
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: playwright-report
          path: frontend/playwright-report/
          retention-days: 14

  docker-build:
    name: Docker images build (no push)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: docker/setup-buildx-action@v3

      - name: Build API image
        uses: docker/build-push-action@v6
        with:
          context: .
          file: Pena_e_Arte.API/Dockerfile
          push: false
          cache-from: type=gha,scope=api
          cache-to: type=gha,mode=max,scope=api

      - name: Build frontend image
        uses: docker/build-push-action@v6
        with:
          context: .
          file: frontend/Dockerfile
          push: false
          cache-from: type=gha,scope=frontend
          cache-to: type=gha,mode=max,scope=frontend
          build-args: |
            VITE_STRIPE_PUBLISHABLE_KEY=pk_test_placeholder
            VITE_CONTACT_EMAIL=support@tattooos.co
            VITE_GOOGLE_CLIENT_ID=placeholder
            VITE_APPLE_CLIENT_ID=placeholder
            VITE_PUBLIC_URL=http://localhost:8081
        # Placeholder build-args only — this job proves the image *builds*, it does not
        # produce anything that gets pushed or deployed. Do not add registry credentials
        # or a `push: true` here; that's the out-of-scope CD work flagged at the top.

  guardrails:
    name: Non-negotiable-rules guardrails
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # full history — gitleaks needs it to scan past commits, not just HEAD

      - name: Secret scan (gitleaks)
        uses: gitleaks/gitleaks-action@v2
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        # Enforces CLAUDE.md rule 4 ("secrets never in source"). If this fires on a false
        # positive (e.g. a Stripe test-mode fixture key in stripe-fixtures/), add a
        # `.gitleaksignore` entry with a comment explaining why it's safe — don't disable the
        # whole job.

      - name: No Console.WriteLine / console.log in production code
        run: |
          set -e
          echo "Checking backend for Console.WriteLine outside tests..."
          if grep -rn "Console\.WriteLine" \
               --include="*.cs" \
               Pena_e_Arte.API Pena_e_Arte.Application Pena_e_Arte.Domain \
               Pena_e_Arte.Infrastructure Pena_e_Arte.Contracts; then
            echo "::error::Console.WriteLine found in production code — use Serilog (rule 5)."
            exit 1
          fi
          echo "Checking frontend for console.log outside tests..."
          if grep -rn "console\.log" \
               --include="*.ts" --include="*.tsx" \
               frontend/src \
             | grep -v "\.test\.\|\.spec\.|/e2e/"; then
            echo "::error::console.log found in production frontend code (rule 5)."
            exit 1
          fi
          echo "Clean."
        # Run this locally against current `main` before wiring it as blocking, per the
        # Guiding Principle above. Backend and frontend genuinely may already be clean since
        # Serilog/sonner-toast patterns are established conventions here — verify, don't assume.

      - name: Unprotected-endpoint heuristic
        run: |
          set -e
          echo "Scanning for Map* endpoint registrations missing .RequireAuthorization()/.AllowAnonymous()..."
          python3 - <<'PYEOF'
          import re, sys, pathlib

          violations = []
          pattern = re.compile(r'\.Map(Get|Post|Put|Patch|Delete)\s*\(')
          for path in pathlib.Path("Pena_e_Arte.API").rglob("*Endpoints.cs"):
              text = path.read_text()
              for m in pattern.finditer(text):
                  # Look ahead up to 300 chars / 6 lines for a guard on the same fluent chain
                  window = text[m.end(): m.end() + 300]
                  if "RequireAuthorization" not in window and "AllowAnonymous" not in window:
                      line_no = text[:m.start()].count("\n") + 1
                      violations.append(f"{path}:{line_no}")

          if violations:
              print("::error::Endpoints missing .RequireAuthorization()/.AllowAnonymous() — rule 2:")
              for v in violations:
                  print(f"  {v}")
              sys.exit(1)
          print("Clean.")
          PYEOF
        # This is a heuristic, not a proof — it will miss endpoints where the guard is applied
        # to the whole `group` rather than the individual Map call, and it doesn't know about
        # architecture.md's approved AllowAnonymous exceptions list vs. genuinely unprotected
        # routes. Run it once against current `main`, hand-check every reported line, and if it's
        # too noisy (high false-positive rate against the `group.MapGet(...).RequireAuthorization()`
        # applied-to-group pattern actually used in this codebase), rewrite the heuristic to also
        # detect a `.RequireAuthorization(...)` call chained on the enclosing `group` variable
        # earlier in the same block, rather than dropping the check. Don't ship a check nobody
        # trusts because it cries wolf.
```

**Before finalizing:** actually run every one of these steps' underlying commands locally
(`dotnet format`, `dotnet build`, both `dotnet test` invocations against a local MySQL 8.4,
`pnpm lint`, `pnpm build`, `pnpm test`, `pnpm test:e2e`, both `docker build` commands, the two
guardrail scripts) before pushing the workflow. A CI file that has never actually executed the
commands it invokes is not verified — "I wrote YAML that looks right" is not the acceptance bar.

---

## Phase 3 — Security scanning: `.github/workflows/codeql.yml`

Separate workflow (CodeQL results live in the Security tab, not the PR checks list the same
way, and it runs on a different, slower cadence than the fast feedback loop in `ci.yml`):

```yaml
name: CodeQL

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]
  schedule:
    - cron: "0 6 * * 1"   # weekly, Monday 06:00 UTC — catches new CVEs in unchanged code

permissions:
  contents: read
  security-events: write

jobs:
  analyze:
    name: Analyze (${{ matrix.language }})
    runs-on: ubuntu-latest
    strategy:
      fail-fast: false
      matrix:
        language: [csharp, javascript-typescript]
    steps:
      - uses: actions/checkout@v4

      - uses: github/codeql-action/init@v3
        with:
          languages: ${{ matrix.language }}

      - name: Build backend
        if: matrix.language == 'csharp'
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Compile for CodeQL (csharp)
        if: matrix.language == 'csharp'
        run: dotnet build "Pena e Arte.slnx" --configuration Release
        # Autobuild is unreliable on multi-project .slnx solutions — build explicitly instead.

      - uses: github/codeql-action/analyze@v3
        with:
          category: "/language:${{ matrix.language }}"
```

---

## Phase 4 — Dependency automation: `.github/dependabot.yml`

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 10
    groups:
      minor-and-patch:
        update-types: ["minor", "patch"]
    commit-message:
      prefix: "chore"

  - package-ecosystem: "npm"
    directory: "/frontend"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 10
    groups:
      minor-and-patch:
        update-types: ["minor", "patch"]
    commit-message:
      prefix: "chore"

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    commit-message:
      prefix: "chore"
```

Grouping minor/patch updates into one PR each keeps this from spamming a dozen individual
PRs a week — major-version bumps stay ungrouped so they get individual review, per standard
Dependabot practice. Every Dependabot PR will run the full `ci.yml` automatically once branch
protection is live (Phase 8) — that's the point of doing dependency updates and CI together.

---

## Phase 5 — PR template: `.github/pull_request_template.md`

```markdown
## What changed

<!-- One or two sentences. -->

## Non-negotiable rules checklist

- [ ] Tenant isolation: any new/changed query touches tenant data only through EF Core global
      query filters, or is explicitly `issuer`-scoped with an `// Approved:` comment
- [ ] RBAC: every new endpoint has `.RequireAuthorization()` with the correct policy, or is
      `/auth` / `/health` / documented in `architecture.md`'s AllowAnonymous exceptions list
- [ ] No PII in logs (names, emails, phone numbers, card data)
- [ ] No secrets committed (env vars / Vault only)
- [ ] Serilog only — no `Console.WriteLine` / `console.log`
- [ ] New endpoints have a FluentValidation validator
- [ ] New Application-layer logic has a test
- [ ] Help Menu / user manual / onboarding tour updated if this is a user-facing change
      (`docs/claude/architecture.md` → "In-App Help Menu" section)

## How this was tested

<!-- Manual steps, or "covered by CI" if the automated suite is sufficient. -->
```

---

## Phase 6 — Baseline triage (do this before wiring blocking gates)

Run locally, against current `main`, and record the raw output:

```bash
dotnet format "Pena e Arte.slnx" --verify-no-changes --verbosity diagnostic
cd frontend && pnpm lint
```

Apply the Guiding Principle decision rule above to each. If `dotnet format` wants to rewrite
files, run `dotnet format "Pena e Arte.slnx"` (no `--verify-no-changes`) once, review the diff
is purely whitespace/style (no behavior change), and commit it separately as
`chore: run dotnet format baseline` *before* the commit that adds the CI workflow — this keeps
the CI-addition diff readable and gives the format gate a clean starting line.

---

## Phase 7 — Docs update

1. Add an entry to `docs/claude/architecture.md`'s Decisions Log:
   > CI: GitHub Actions (`ci.yml`, `codeql.yml`) added 2026-07-26. Backend job runs
   > `dotnet format --verify-no-changes` + unit + integration tests against a `mysql:8.4`
   > container matching `docker-compose.yml`. Frontend job runs lint + typecheck + build +
   > Vitest + Playwright. `docker-build` validates both Dockerfiles build (no push — no
   > registry configured yet). `guardrails` job enforces secret-scanning (gitleaks),
   > no-`Console.WriteLine`/`console.log`, and an endpoint-authorization heuristic. All are
   > required status checks on `main` (see branch protection). CD/deployment is not yet
   > automated — tracked as a follow-up.
2. If any gate in Phase 2 was shipped as `continue-on-error: true` per the Guiding Principle,
   list it explicitly here with what's blocking it from going fully blocking, so it isn't
   silently forgotten.

---

## Phase 8 — Branch protection (manual — cannot be done by editing repo files)

A workflow file alone enforces nothing; someone with repo admin access must turn these checks
into required status checks. These steps are for Phi (or whoever has admin on
`471k/pena-e-arte`) to do in the GitHub UI **after** the workflow files above have been merged
to `main` and have run at least once (GitHub only lists a check as selectable in the branch
protection UI once it has reported at least one run):

1. GitHub → repo → **Settings → Branches → Add branch protection rule** → branch name
   pattern `main`.
2. Enable **Require a pull request before merging** (optionally require 1 approval if there's
   more than one engineer).
3. Enable **Require status checks to pass before merging**, then **Require branches to be up
   to date before merging**, and select these checks (exact names as they'll appear once the
   workflows have run once):
   - `Backend — build, format, test`
   - `Frontend — lint, typecheck, build, unit test, e2e`
   - `Docker images build (no push)`
   - `Non-negotiable-rules guardrails`
   - `Analyze (csharp)`
   - `Analyze (javascript-typescript)`
4. Enable **Require conversation resolution before merging**.
5. Under **Settings → Code security and analysis**, enable GitHub's native **secret scanning**
   and, if available on the current plan, **push protection** — this is separate from and
   complementary to the gitleaks step in `guardrails` (gitleaks runs in-CI on push; native
   push protection blocks the `git push` itself before the commit even lands).
6. Do **not** enable "Allow force pushes" or "Allow deletions" on `main`.

---

## Phase 9 — Verification

Do not consider this done until you have:

1. Opened a real (draft is fine) PR against `main` from a scratch branch and watched all five
   checks (`backend`, `frontend`, `docker-build`, `guardrails`, both `codeql` matrix legs)
   actually run and go green in the Checks tab — not just "the YAML has no syntax errors."
2. Deliberately broken one thing per job on a throwaway branch to confirm each gate actually
   fails closed: e.g., add a stray `Console.WriteLine` (guardrails should fail), introduce a
   type error (frontend build should fail), break a `.csproj` reference (docker-build should
   fail), misformat a `.cs` file (format check should fail if wired blocking). Revert these
   before merging anything real — they're verification-only.
3. Confirmed the integration test job actually talks to the `ci-mysql` container and isn't
   silently skipping/no-oping (check the `.trx`/coverage artifact has a nonzero test count).
4. Written the final summary covering: which gates are blocking vs. `continue-on-error` and
   why, the exact `global.json` SDK version used, and the branch-protection steps in Phase 8
   that still need a human with admin access to click through (you cannot do this part
   yourself from a file-editing session).

---

## Hard Rules

1. No deployment/CD in this change — build-and-test only, plus the Docker *build* (not push)
   validation. Flag CD as a follow-up, don't build it.
2. No new secrets provisioned that aren't actually consumed by a test (see the Working
   Context note on why the integration tests need zero external secrets).
3. Every blocking gate must have been run and observed green (or explicitly downgraded to
   `continue-on-error` with a stated reason) before this is considered complete — no gate
   ships in an unknown state.
4. Match the exact tool versions already pinned in `Pena_e_Arte.API/Dockerfile` and
   `frontend/Dockerfile` (.NET 10 / Node 24 / pnpm 11.5.1) — don't let CI silently drift onto
   a different toolchain than what actually ships.
5. `guardrails` heuristics (the endpoint-authorization script especially) must be validated
   against the real codebase, not shipped on faith — a check with a high false-positive rate
   that gets `# noqa`'d into irrelevance the following week is worse than no check.
