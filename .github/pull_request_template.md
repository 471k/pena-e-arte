## What changed

<!-- One or two sentences. -->

## Non-negotiable rules checklist

- [ ] Tenant isolation: any new/changed query touches tenant data only through EF Core global
      query filters, or is explicitly `admin`-scoped with an `// Approved:` comment
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
