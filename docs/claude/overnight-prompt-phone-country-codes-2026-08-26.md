# Overnight Prompt — Country Codes on Phone Input Fields

**Goal:** Every phone-number field in the app today is a bare free-text `<input type="tel">`
with no country code, no format enforcement, and (in two of the three cases) **no backend
format validation at all**. This prompt replaces all three real phone-entry surfaces with a
shared `PhoneInput` component (country-code select + national number field, producing a
canonical E.164 string) and adds matching FluentValidation rules on the backend.

This is not cosmetic. `NotificationService.SendSmsAsync` passes the stored phone string
straight into Twilio's `PhoneNumber` type:

```csharp
to: new PhoneNumber(to)
```

Twilio's SMS API requires E.164 (`+<countrycode><number>`, e.g. `+351912345678`). Today
nothing guarantees that shape: `CreateClientValidator` only checks `MaximumLength(20)`,
`CreateManualReminderValidator` only checks `NotEmpty().MaximumLength(20)`, and
`UpdateMyStudioValidator` (in `UpdateMyStudioCommand.cs`) has **no rule for `PhoneNumber` at
all** — a studio's contact number can be saved as literally anything, unvalidated, both from
the form (client-side `max(30)` only) and from a direct API call. A client/reminder recipient
who types a national-format number with no `+` and no country code (e.g. `912 345 678`) will
have their manual SMS reminder fail at the Twilio call with no `to`-format guard anywhere
upstream. This prompt closes that gap as a side effect of adding the country-code picker, not
as a separate change — the two are the same fix.

Confirmed by reading the source (not assumed): there are exactly **three** real phone-entry
surfaces in the app. Every other file a `phone`/`Phone` search turns up (`ClientCard.tsx`,
`ClientDetailPage.tsx`'s display block, `ClientListPage.tsx`, `MyProfilePage.tsx`,
`ChannelBadge.tsx`, `StudioPortfolioPage.tsx`, `PrivacyPolicyPage.tsx`, `helpContent.ts`) only
**displays** a phone value or is unrelated copy — none of them is an editable input. See
"Out of Scope" for why `MyProfilePage.tsx` (a client's own phone) isn't one of the three —
it's a real, separate gap, flagged rather than silently rolled into this change.

No backend schema/migration change: `Client.Phone` (`varchar(20)`) and
`ManualReminder.RecipientPhone` (`varchar(20)`, required) already comfortably fit the longest
possible E.164 string (`+` plus up to 15 digits = 16 chars). `Studio.PhoneNumber` is
`longtext` with no configured max — left as-is; the new FluentValidation `Matches()` rule
functionally caps its effective length without a migration.

All changes must pass `dotnet build`, `dotnet test`, `pnpm tsc --noEmit`, `pnpm lint`, and
`pnpm test --run` before the session ends.

---

## Read First

1. `CLAUDE.md`
2. `docs/claude/frontend.md`
3. `docs/claude/backend.md`
4. `docs/claude/conventions.md`
5. `docs/claude/architecture.md` — specifically the `## Decisions Log` table (Section 9 below
   adds a new row at its end, directly before the `---` that precedes `## Issuer QA Pass —
   2026-07-01`)

---

## Source Files to Read Before Starting

Read each file in full before changing anything:

- `frontend/src/features/clients/components/CreateClientPage.tsx`
- `frontend/src/features/studios/components/StudioProfilePage.tsx`
- `frontend/src/features/reminders/components/ReminderDialog.tsx`
- `frontend/src/shared/components/ui/select.tsx` — Radix `Select` primitive being reused (it
  has built-in typeahead: typing while the listbox is open/focused jumps to a matching item —
  this is why the new country picker does not need a search box or a new Combobox/cmdk
  dependency)
- `frontend/src/shared/components/ui/location-picker.tsx` — the closest existing precedent for
  a compound, non-`register()`-able shared input driven through RHF's `Controller`
- `frontend/src/shared/components/ui/field-hint.tsx`
- `frontend/src/shared/utils/googleMaps.ts` (if present from a prior prompt) or any file in
  `shared/utils/` — for the shape/style of a small pure-function utility file with an
  explanatory doc comment on the non-obvious parts
- `Pena_e_Arte.Application/Clients/Validators/CreateClientValidator.cs`
- `Pena_e_Arte.Application/Studios/Commands/UpdateMyStudioCommand.cs` — note
  `UpdateMyStudioValidator` is a second class **in this same file**, not a separate file in
  `Validators/` (unlike `RegisterStudioValidator`) — this is the codebase's actual existing
  placement for this specific validator; do not move it into a new file as part of this change
- `Pena_e_Arte.Application/Studios/Validators/RegisterStudioValidator.cs` — note its
  `NiptFormat` regex is declared as a `private static readonly Regex` field local to the class,
  and `UpdateMyStudioValidator` independently redeclares the identical regex rather than
  sharing a constant — this prompt's new `E164Format` regex follows that exact same
  already-established (if duplicative) pattern across its three validators, deliberately, for
  consistency with existing code rather than introducing a new shared-constants convention
  unprompted
- `Pena_e_Arte.Application/Reminders/Validators/CreateManualReminderValidator.cs`
- `Pena_e_Arte.Application/Reminders/Commands/CreateManualReminderCommand.cs`
- `Pena_e_Arte.Infrastructure/Services/NotificationService.cs` — read `SendSmsAsync`; do not
  change it (see "Out of Scope")
- `frontend/src/features/help/helpContent.ts` — read the `owner-clients-add` and
  `owner-studio-profile` entries in full before editing (Section 8)
- `frontend/public/user-manual/index.html` — read the `#owner-clients-add`,
  `#owner-studio-profile`, and the artist Schedule/Quick-Reminder section in full before
  editing (Section 8)
- `frontend/package.json` — confirm current versions before adding the new dependency
  (`react@^19.2.6`, `react-hook-form@^7.77.0`, `zod@^4.4.3`, `typescript@~6.0.2` at the time
  this prompt was written; re-confirm, don't assume they haven't moved)

---

## Files to Change

| File | What changes |
|---|---|
| `frontend/package.json` | **New dependency** — `libphonenumber-js` |
| `frontend/src/shared/utils/phoneCountries.ts` | **New file** — country list, calling codes, flag emoji, default country |
| `frontend/src/shared/utils/__tests__/phoneCountries.test.ts` | **New file** |
| `frontend/src/shared/utils/phoneValidation.ts` | **New file** — `isValidE164Phone`, shared error copy |
| `frontend/src/shared/utils/__tests__/phoneValidation.test.ts` | **New file** |
| `frontend/src/shared/components/ui/phone-input.tsx` | **New file** — the shared `PhoneInput` component |
| `frontend/src/shared/components/ui/__tests__/phone-input.test.tsx` | **New file** |
| `frontend/src/features/clients/components/CreateClientPage.tsx` | Swap plain phone `Input` for `PhoneInput`; schema validation |
| `frontend/src/features/clients/__tests__/CreateClientPage.test.tsx` | New/updated tests for the phone field |
| `frontend/src/features/studios/components/StudioProfilePage.tsx` | Swap plain phone `Input` for `PhoneInput`; schema validation; destructure `control` |
| `frontend/src/features/studios/__tests__/StudioProfilePage.test.tsx` | New/updated tests for the phone field |
| `frontend/src/features/reminders/components/ReminderDialog.tsx` | Swap raw-contact phone `Input` for `PhoneInput`; extend `canSubmit`; inline validity hint |
| `frontend/src/features/reminders/__tests__/ReminderDialog.test.tsx` | New/updated tests for the phone field |
| `Pena_e_Arte.Application/Clients/Validators/CreateClientValidator.cs` | Add E.164 `Matches()` rule for `Phone` |
| `Pena_e_Arte.Application/Studios/Commands/UpdateMyStudioCommand.cs` | Add E.164 `Matches()` rule to `UpdateMyStudioValidator` for `PhoneNumber` (currently has none) |
| `Pena_e_Arte.Application/Reminders/Validators/CreateManualReminderValidator.cs` | Add E.164 `Matches()` rule for `RecipientPhone` |
| `tests/Pena_e_Arte.UnitTests/Clients/CreateClientValidatorTests.cs` | New cases (create the file if it doesn't already exist — check first) |
| `tests/Pena_e_Arte.UnitTests/Studios/UpdateMyStudioValidatorTests.cs` | New cases (create if it doesn't already exist — check first) |
| `tests/Pena_e_Arte.UnitTests/Reminders/CreateManualReminderValidatorTests.cs` | New cases (create if it doesn't already exist — check first) |
| `frontend/src/features/help/helpContent.ts` | Wording update to the `owner-clients-add` and `owner-studio-profile` steps |
| `frontend/public/user-manual/index.html` | Wireframe + step updates for Add Client, Studio Profile, and the artist Quick Reminder section |
| `docs/claude/architecture.md` | New Decisions Log row |

`frontend/src/features/help/tours/*.ts` is **deliberately not touched** — see Section 8 for
why, stated explicitly rather than silently skipped, per `CLAUDE.md` rule 7.

---

## Section 1 — `frontend/src/shared/utils/phoneCountries.ts` (new file)

```bash
cd frontend && pnpm add libphonenumber-js
```

Use the `/min` entry point everywhere in this prompt (`libphonenumber-js/min`), not the
default `libphonenumber-js` import — it ships a smaller metadata bundle (mobile+fixed-line
patterns only, no extended/pager/voip subtype data this app has no use for), which matters
for a client-facing bundle. Record the actual resolved version in the Decisions Log row
(Section 9) — don't guess it.

```ts
import { getCountries, getCountryCallingCode } from "libphonenumber-js/min";

export type PhoneCountryCode = string; // ISO 3166-1 alpha-2, e.g. "PT"

export interface PhoneCountryOption {
  code:        PhoneCountryCode;
  name:        string;
  callingCode: string; // e.g. "351" — no leading '+'
}

const regionNames = new Intl.DisplayNames(["en"], { type: "region" });

/**
 * Every country libphonenumber-js has calling-code metadata for, sorted by display name.
 * Built once at module load — this list is static per app version, not per render.
 */
export const PHONE_COUNTRIES: PhoneCountryOption[] = getCountries()
  .map((code) => ({
    code,
    name: regionNames.of(code) ?? code,
    callingCode: getCountryCallingCode(code),
  }))
  .sort((a, b) => a.name.localeCompare(b.name));

/**
 * Default selected country for a fresh, empty PhoneInput. Portugal — every existing phone
 * placeholder and test fixture in this codebase already uses a +351 number
 * (CreateClientPage's and StudioProfilePage's placeholders, ReminderDialog's placeholder,
 * GetPublicStudioHandlerTests' fixture), so this matches the app's established implicit
 * default rather than introducing a new one. Not derived from browser locale or the studio's
 * own city/country — that's a real, separate enhancement, flagged in "Out of Scope" below,
 * not assumed here.
 */
export const DEFAULT_PHONE_COUNTRY: PhoneCountryCode = "PT";

/** Converts an ISO 3166-1 alpha-2 code to its flag emoji via Unicode regional indicator symbols. */
export function flagEmoji(code: PhoneCountryCode): string {
  return code
    .toUpperCase()
    .replace(/./g, (char) => String.fromCodePoint(127397 + char.charCodeAt(0)));
}
```

**TypeScript `lib` check:** `Intl.DisplayNames` needs `"ES2021"` (or later) in
`frontend/tsconfig.json`'s `compilerOptions.lib` array. Read the current `lib` list first —
only add what's actually missing; do not widen the target more than necessary. If
`pnpm tsc --noEmit` is clean without any change, leave `tsconfig.json` untouched entirely and
say so in the Decisions Log row rather than adding an unneeded entry.

Create `frontend/src/shared/utils/__tests__/phoneCountries.test.ts` (match this repo's
existing `shared/utils/__tests__` `describe`/`it` style) covering:

- `PHONE_COUNTRIES` contains an entry for `"PT"` with `callingCode: "351"`.
- `PHONE_COUNTRIES` is sorted by `name` (assert `[...PHONE_COUNTRIES].sort(...)` deep-equals
  `PHONE_COUNTRIES`, don't hand-write the full expected order).
- `PHONE_COUNTRIES` has no duplicate `code` values.
- `flagEmoji("PT")` → `"🇵🇹"`.
- `flagEmoji("us")` (lowercase input) → `"🇺🇸"`.

---

## Section 2 — `frontend/src/shared/utils/phoneValidation.ts` (new file)

```ts
import { isValidPhoneNumber } from "libphonenumber-js/min";

export const PHONE_ERROR_MESSAGE = "Enter a valid phone number, e.g. +351 912 345 678";

/**
 * Empty/null/undefined is treated as valid — every phone field in this app is optional at
 * the model level (Client.Phone, Studio.PhoneNumber are both nullable). Callers pair this
 * with their own `NotEmpty()`/zod-required rule for the one field that IS required
 * (CreateManualReminderCommand.RecipientPhone, raw-contact path only).
 */
export function isValidE164Phone(value: string | null | undefined): boolean {
  if (!value) return true;
  return isValidPhoneNumber(value);
}
```

Create `frontend/src/shared/utils/__tests__/phoneValidation.test.ts`:

- `isValidE164Phone("")` / `isValidE164Phone(null)` / `isValidE164Phone(undefined)` → `true`.
- `isValidE164Phone("+351912345678")` → `true`.
- `isValidE164Phone("912345678")` (no country code) → `false`.
- `isValidE164Phone("+35191234")` (too short to be a real PT number) → `false`.
- `isValidE164Phone("not a phone number")` → `false`.

---

## Section 3 — `frontend/src/shared/components/ui/phone-input.tsx` (new file)

A controlled, single-`value` (E.164 string) compound input: a country-code `Select` next to a
national-number `Input`. Modeled on how `location-picker.tsx` is wired into a form through
`Controller` rather than `register()` — a phone number here is one form field but two visual
controls, the same shape problem `location-picker.tsx` already solves for lat/lng.

```tsx
"use client";
import { useRef, useState } from "react";
import { AsYouType, getCountryCallingCode, parsePhoneNumberFromString } from "libphonenumber-js/min";
import { Input } from "./input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "./select";
import { DEFAULT_PHONE_COUNTRY, PHONE_COUNTRIES, flagEmoji, type PhoneCountryCode } from "@/shared/utils/phoneCountries";
import { cn } from "@/shared/utils/cn";

interface PhoneInputProps {
  id?: string;
  value: string;
  onChange: (e164: string) => void;
  onBlur?: () => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}

function deriveState(value: string): { country: PhoneCountryCode; nationalText: string } {
  if (!value) return { country: DEFAULT_PHONE_COUNTRY, nationalText: "" };
  const parsed = parsePhoneNumberFromString(value, DEFAULT_PHONE_COUNTRY);
  if (parsed) {
    return {
      country: (parsed.country as PhoneCountryCode) ?? DEFAULT_PHONE_COUNTRY,
      nationalText: parsed.formatNational(),
    };
  }
  // Legacy freeform data entered before this component existed (e.g. a locally-formatted
  // number with no leading '+') — libphonenumber-js couldn't parse it even with a default
  // country hint. Surface it verbatim in the national-number field instead of discarding it,
  // so whoever owns this record can see and correct it, rather than it silently vanishing.
  return { country: DEFAULT_PHONE_COUNTRY, nationalText: value };
}

export function PhoneInput({
  id, value, onChange, onBlur, placeholder, disabled, className,
  "aria-invalid": ariaInvalid, "aria-describedby": ariaDescribedBy,
}: PhoneInputProps) {
  const initial = useRef(deriveState(value));
  const lastEmitted = useRef(value);
  const [country, setCountry] = useState<PhoneCountryCode>(initial.current.country);
  const [nationalText, setNationalText] = useState(initial.current.nationalText);

  // Resync from outside (e.g. RHF's `reset()` after the parent form's data loads
  // asynchronously) only when the prop actually changed from what this component itself
  // last emitted — otherwise every keystroke's own onChange would immediately bounce back
  // through here and fight the user's typing.
  if (value !== lastEmitted.current) {
    lastEmitted.current = value;
    const derived = deriveState(value);
    if (derived.country !== country) setCountry(derived.country);
    if (derived.nationalText !== nationalText) setNationalText(derived.nationalText);
  }

  function emit(next: string) {
    lastEmitted.current = next;
    onChange(next);
  }

  function handleCountryChange(next: string) {
    const nextCountry = next as PhoneCountryCode;
    setCountry(nextCountry);
    const digits = nationalText.replace(/[^\d]/g, "");
    const formatted = digits ? new AsYouType(nextCountry).input(digits) : "";
    setNationalText(formatted);
    const parsed = parsePhoneNumberFromString(formatted, nextCountry);
    emit(parsed?.isValid() ? parsed.number : digits ? `+${getCountryCallingCode(nextCountry)}${digits}` : "");
  }

  function handleNationalChange(raw: string) {
    const digits = raw.replace(/[^\d]/g, "");
    const formatted = digits ? new AsYouType(country).input(digits) : "";
    setNationalText(formatted);
    // Prefer libphonenumber's own canonical E.164 once the number is actually complete and
    // valid (this correctly drops a redundant national trunk prefix, e.g. a leading '0' in
    // countries that dial that way domestically). While the user is still mid-type, or if
    // what they've typed doesn't parse as valid for the selected country, fall back to a
    // naive '+<callingCode><digits>' concatenation — it does not need to be a *correct* E.164
    // number, only a *distinct, genuinely invalid one* so the form's isValidE164Phone check
    // fails and shows an error instead of the field silently going empty on submit.
    const parsed = parsePhoneNumberFromString(formatted, country);
    emit(parsed?.isValid() ? parsed.number : digits ? `+${getCountryCallingCode(country)}${digits}` : "");
  }

  return (
    <div className={cn("flex gap-2", className)}>
      <Select value={country} onValueChange={handleCountryChange} disabled={disabled}>
        <SelectTrigger className="w-[120px] shrink-0 min-h-[44px]" aria-label="Country code">
          <SelectValue>{flagEmoji(country)} +{getCountryCallingCode(country)}</SelectValue>
        </SelectTrigger>
        <SelectContent className="max-h-72">
          {PHONE_COUNTRIES.map((c) => (
            <SelectItem key={c.code} value={c.code}>
              {flagEmoji(c.code)} {c.name} (+{c.callingCode})
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Input
        id={id}
        type="tel"
        inputMode="tel"
        value={nationalText}
        onChange={(e) => handleNationalChange(e.target.value)}
        onBlur={onBlur}
        placeholder={placeholder ?? "912 345 678"}
        disabled={disabled}
        aria-invalid={ariaInvalid}
        aria-describedby={ariaDescribedBy}
        className="flex-1 min-h-[44px]"
      />
    </div>
  );
}
```

No search box in the country `Select` — Radix `Select`'s built-in typeahead (type while
open/focused to jump to a matching item) covers the "200 countries in a dropdown" usability
concern without adding a new Combobox/cmdk dependency, consistent with this codebase's
existing "use the shadcn primitive before writing something new" convention.

Create `frontend/src/shared/components/ui/__tests__/phone-input.test.tsx`:

- Renders with `value=""` → country select shows `+351` (default), national input is empty.
- Renders with `value="+351912345678"` → national input shows the nationally-formatted text
  (`912 345 678`), country select shows `+351`.
- Renders with `value="+447911123456"` → country select shows `+44` (derives country from the
  E.164 value, not just the default).
- Renders with a legacy freeform `value="0912-345-678"` (unparseable) → country select falls
  back to `+351`, national input shows the raw legacy text verbatim.
- Typing a valid PT national number into the national input calls `onChange` with the correct
  full E.164 string (`+351912345678`) once complete.
- Typing an incomplete number calls `onChange` with a non-empty, non-valid string (assert the
  emitted value fails `isValidPhoneNumber` from `libphonenumber-js/min`, not a hardcoded
  string — the exact fallback shape is an implementation detail).
- Switching the country select re-emits `onChange` with the new country's calling code applied
  to whatever digits were already typed.
- `aria-invalid` and `aria-describedby` passed as props land on the national `Input`, not the
  `Select`.

---

## Section 4 — `CreateClientPage.tsx`

### 4-A: Import and schema

```ts
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { isValidE164Phone, PHONE_ERROR_MESSAGE } from "@/shared/utils/phoneValidation";
```

Change:

```ts
  phone:     z.string().optional(),
```

to:

```ts
  phone:     z.string().refine(isValidE164Phone, PHONE_ERROR_MESSAGE).optional(),
```

`control` is already destructured from `useForm()` in this file (used for the `artistId`
`Controller` below) — no change needed there.

### 4-B: JSX

Find:

```tsx
          <div className="space-y-1.5">
            <Label htmlFor="phone">Phone (optional)</Label>
            <Input
              id="phone"
              type="tel"
              placeholder="e.g. +351 912 345 678"
              {...register("phone")}
            />
          </div>
```

Replace with:

```tsx
          <div className="space-y-1.5">
            <Label htmlFor="phone">Phone (optional)</Label>
            <Controller
              control={control}
              name="phone"
              render={({ field }) => (
                <PhoneInput
                  id="phone"
                  value={field.value ?? ""}
                  onChange={field.onChange}
                  onBlur={field.onBlur}
                  aria-invalid={!!errors.phone}
                  aria-describedby={errors.phone ? "phone-error" : undefined}
                />
              )}
            />
            {errors.phone && (
              <p id="phone-error" className="text-xs text-destructive">{errors.phone.message}</p>
            )}
          </div>
```

`onSave`'s existing `phone: values.phone?.trim() || null` line is unchanged — `PhoneInput`
always emits either `""` or a string, so the existing trim-and-null-coalesce still behaves
correctly.

Update `frontend/src/features/clients/__tests__/CreateClientPage.test.tsx`: find its existing
phone-field test(s) (read the file first — match its existing render/query helper names, don't
assume) and add cases for: leaving phone empty still submits successfully; typing an invalid
number and submitting shows `PHONE_ERROR_MESSAGE`; typing a valid number submits with the
correct E.164 value in the mutation call.

---

## Section 5 — `StudioProfilePage.tsx`

### 5-A: Import, schema, and `control`

```ts
import { Controller } from "react-hook-form";
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { isValidE164Phone, PHONE_ERROR_MESSAGE } from "@/shared/utils/phoneValidation";
```

Change:

```ts
  phoneNumber:     z.string().max(30, "Max 30 characters").optional(),
```

to:

```ts
  phoneNumber:     z.string().refine(isValidE164Phone, PHONE_ERROR_MESSAGE).optional(),
```

(The `max(30)` cap is replaced, not kept alongside — a valid E.164 string is at most 16
characters, so `isValidE164Phone` is already the tighter, more meaningful constraint; keeping
both would just be redundant and could produce a confusing double error state.)

Change:

```ts
  const { register, handleSubmit, reset, watch, setValue, formState: { errors, isDirty } } =
    useForm<FormValues>({ resolver: zodResolver(schema) });
```

to:

```ts
  const { register, handleSubmit, control, reset, watch, setValue, formState: { errors, isDirty } } =
    useForm<FormValues>({ resolver: zodResolver(schema) });
```

### 5-B: JSX

Find:

```tsx
              <div className="space-y-1.5">
                <Label htmlFor="phoneNumber">Phone number (optional)</Label>
                <Input
                  id="phoneNumber"
                  type="tel"
                  placeholder="+351 912 345 678"
                  {...register("phoneNumber")}
                  aria-invalid={!!errors.phoneNumber}
                  aria-describedby={errors.phoneNumber ? "phoneNumber-error" : undefined}
                />
                {errors.phoneNumber && (
                  <p id="phoneNumber-error" className="text-xs text-destructive">{errors.phoneNumber.message}</p>
                )}
              </div>
```

Replace with:

```tsx
              <div className="space-y-1.5">
                <Label htmlFor="phoneNumber">Phone number (optional)</Label>
                <Controller
                  control={control}
                  name="phoneNumber"
                  render={({ field }) => (
                    <PhoneInput
                      id="phoneNumber"
                      value={field.value ?? ""}
                      onChange={field.onChange}
                      onBlur={field.onBlur}
                      aria-invalid={!!errors.phoneNumber}
                      aria-describedby={errors.phoneNumber ? "phoneNumber-error" : undefined}
                    />
                  )}
                />
                {errors.phoneNumber && (
                  <p id="phoneNumber-error" className="text-xs text-destructive">{errors.phoneNumber.message}</p>
                )}
              </div>
```

The `reset({ ..., phoneNumber: studio.phoneNumber ?? "", ... })` call already in this file's
`useEffect` is unchanged — `PhoneInput` derives its own displayed country/national text from
whatever E.164 (or legacy freeform) string `reset()` puts into the form value.

Update `frontend/src/features/studios/__tests__/StudioProfilePage.test.tsx` with the same
shape of cases as Section 4's `CreateClientPage` tests, adjusted for this page's save flow.

---

## Section 6 — `ReminderDialog.tsx`

This dialog does not use `react-hook-form` — it's plain `useState` + a `canSubmit` boolean
gate, so the wiring is a normal controlled component, not a `Controller`.

### 6-A: Import

```ts
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { isValidE164Phone, PHONE_ERROR_MESSAGE } from "@/shared/utils/phoneValidation";
```

### 6-B: JSX

Find:

```tsx
              <div className="space-y-1.5">
                <Label htmlFor="reminder-recipient-phone">Phone</Label>
                <Input
                  id="reminder-recipient-phone"
                  type="tel"
                  value={recipientPhone}
                  onChange={(e) => setRecipientPhone(e.target.value)}
                  placeholder="+351 900 000 000"
                  maxLength={20}
                />
              </div>
```

Replace with:

```tsx
              <div className="space-y-1.5">
                <Label htmlFor="reminder-recipient-phone">Phone</Label>
                <PhoneInput
                  id="reminder-recipient-phone"
                  value={recipientPhone}
                  onChange={setRecipientPhone}
                  aria-invalid={recipientPhone.length > 0 && !isValidE164Phone(recipientPhone)}
                  aria-describedby={
                    recipientPhone.length > 0 && !isValidE164Phone(recipientPhone)
                      ? "reminder-recipient-phone-error"
                      : undefined
                  }
                />
                {recipientPhone.length > 0 && !isValidE164Phone(recipientPhone) && (
                  <p id="reminder-recipient-phone-error" className="text-xs text-destructive">
                    {PHONE_ERROR_MESSAGE}
                  </p>
                )}
              </div>
```

### 6-C: `canSubmit`

Find:

```tsx
  const canSubmit = (isRawContact
    ? recipientName.trim().length > 0 && recipientPhone.trim().length > 0
    : needsArtistPicker
    ? pickedArtistId.length > 0
    : true) && (!scheduleLater || isValidDateTimeLocal(scheduledFor));
```

Replace with:

```tsx
  const canSubmit = (isRawContact
    ? recipientName.trim().length > 0 && isValidE164Phone(recipientPhone) && recipientPhone.trim().length > 0
    : needsArtistPicker
    ? pickedArtistId.length > 0
    : true) && (!scheduleLater || isValidDateTimeLocal(scheduledFor));
```

(`isValidE164Phone("")` is `true` — the extra `recipientPhone.trim().length > 0` term is kept
so an untouched empty field still disables submit for this specific required-phone path,
exactly as it did before.)

Update `frontend/src/features/reminders/__tests__/ReminderDialog.test.tsx`: add cases for the
raw-contact path — submit button stays disabled while the phone is empty or invalid, becomes
enabled once a valid E.164 number is entered, and the inline error text appears/disappears
correctly.

---

## Section 7 — Backend validators

Each of the three rules below uses the identical E.164 regex, declared independently as a
`private static readonly Regex` field in each validator class — see the "Source Files to Read"
note above on why this mirrors the existing `NiptFormat` duplication pattern rather than
introducing a new shared-constants file.

```csharp
private static readonly Regex E164Format = new(@"^\+[1-9]\d{1,14}$", RegexOptions.Compiled);
```

This is the canonical ITU E.164 shape: a `+`, then 2–15 digits total, the first of which is
non-zero.

### 7-A: `CreateClientValidator.cs`

Change:

```csharp
        RuleFor(x => x.Request.Phone).MaximumLength(20).When(x => x.Request.Phone is not null);
```

to:

```csharp
        RuleFor(x => x.Request.Phone)
            .MaximumLength(20)
            .Matches(E164Format)
            .WithMessage("Phone must be in international format, e.g. +351912345678.")
            .When(x => x.Request.Phone is not null);
```

Add `using System.Text.RegularExpressions;` at the top if not already present, and the
`E164Format` field declaration inside the class.

### 7-B: `UpdateMyStudioCommand.cs` — `UpdateMyStudioValidator`

Add a new rule (there is currently none for `PhoneNumber` in this class at all):

```csharp
        RuleFor(x => x.Request.PhoneNumber)
            .Matches(E164Format)
            .WithMessage("Phone must be in international format, e.g. +351912345678.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.PhoneNumber));
```

Add the `E164Format` field to `UpdateMyStudioValidator` (this file already has
`using System.Text.RegularExpressions;` and a `NiptFormat` field for its own `Nipt` rule — put
`E164Format` alongside it).

### 7-C: `CreateManualReminderValidator.cs`

Change:

```csharp
        RuleFor(x => x.Request.RecipientPhone)
            .NotEmpty().MaximumLength(20)
            .When(x => x.Request.AppointmentId is null && x.Request.ClientId is null);
```

to:

```csharp
        RuleFor(x => x.Request.RecipientPhone)
            .NotEmpty().MaximumLength(20)
            .Matches(E164Format)
            .WithMessage("Phone must be in international format, e.g. +351912345678.")
            .When(x => x.Request.AppointmentId is null && x.Request.ClientId is null);
```

Add `using System.Text.RegularExpressions;` and the `E164Format` field.

### 7-D: Backend tests

Check first whether `tests/Pena_e_Arte.UnitTests/Clients/CreateClientValidatorTests.cs`,
`tests/Pena_e_Arte.UnitTests/Studios/UpdateMyStudioValidatorTests.cs`, and
`tests/Pena_e_Arte.UnitTests/Reminders/CreateManualReminderValidatorTests.cs` already exist —
if so, add cases to them; if not, create them following this repo's existing FluentValidation
test style (`_validator.TestValidate(command)`, `.ShouldHaveValidationErrorFor(...)` /
`.ShouldNotHaveValidationErrorFor(...)` — check an existing validator test file for the exact
pattern in use, e.g. anything under `tests/Pena_e_Arte.UnitTests/Studios/` for
`RegisterStudioValidator`). For each of the three validators, cover:

- A valid E.164 number (`"+351912345678"`) → no error.
- A national-format number with no `+` (`"912345678"`) → validation error.
- `null`/empty (where the field is optional) → no error.
- A string that is not phone-shaped at all (`"not a phone"`) → validation error.
- (Reminders only) an empty `RecipientPhone` on the raw-contact path → the existing
  `NotEmpty` error, not the new format error — confirm the two rules don't produce a confusing
  double message for the same empty-string case.

---

## Section 8 — Help sync

Per `CLAUDE.md` rule 7, every feature must update Help — this section states exactly what does
and does not apply here.

### 8-A: `frontend/src/features/help/helpContent.ts`

`owner-clients-add` — change:

```ts
      "Optionally enter a phone number.",
```

to:

```ts
      "Optionally enter a phone number — pick the country from the dropdown next to the field, then type the number without the country code.",
```

`owner-studio-profile` — change:

```ts
      "Click \"Edit\" and update your studio name, address/city, phone number, or description.",
```

to:

```ts
      "Click \"Edit\" and update your studio name, address/city, phone number (pick the country from the dropdown, then type the number), or description.",
```

No other `helpContent.ts` entries reference an editable phone field — the reminders entries
(`owner-...`/artist Quick Reminder guides, ids containing "reminder") describe the *behavior*
of sending a reminder, not the shape of the phone field itself, and don't need wording changes
for a UI-only change to how the number is entered.

### 8-B: Onboarding tours — deliberately not touched

`frontend/src/features/help/tours/*.ts` steps target nav elements and page-level entry points
(`data-tour="..."` attributes on layout nav items, buttons), never individual form fields —
confirmed by reading every tour file; none references phone. No tour step needs updating.

### 8-C: `frontend/public/user-manual/index.html`

**Add Client wireframe** (`#owner-clients-add` section) — change the phone row's wireframe
text and figcaption:

```html
<text x="280" y="210" font-size="9" fill-opacity="0.6">Phone (optional)</text><rect x="280" y="216" width="200" height="20" rx="4" fill="currentColor" fill-opacity="0.05"/>
```

to:

```html
<text x="280" y="210" font-size="9" fill-opacity="0.6">Phone (optional)</text><rect x="280" y="216" width="45" height="20" rx="4" fill="currentColor" fill-opacity="0.05"/><text x="286" y="230" font-size="8">🇵🇹+351</text><rect x="330" y="216" width="150" height="20" rx="4" fill="currentColor" fill-opacity="0.05"/>
```

(splits the single 200px-wide phone box into a narrow country-code box and a wider
national-number box, same visual split `PhoneInput` renders).

Update the step text. Change:

```html
<li>Optionally add a <span class="step-title">Phone</span> number.</li>
```

to:

```html
<li>Optionally add a <span class="step-title">Phone</span> number — choose the country from the dropdown (defaults to Portugal), then type the number.</li>
```

**Studio Profile wireframe** (`#owner-studio-profile` section) — change:

```html
<text x="280" y="164" font-size="8" fill-opacity="0.6">Phone · Instagram</text>
```

to:

```html
<text x="280" y="164" font-size="8" fill-opacity="0.6">Phone (country + number) · Instagram</text>
```

Update the step text. Change:

```html
<li>Update <span class="step-title">Studio name</span>, <span class="step-title">Phone</span>, and <span class="step-title">Location</span> (drag the map pin), then <span class="step-title">Save changes</span>.</li>
```

to:

```html
<li>Update <span class="step-title">Studio name</span>, <span class="step-title">Phone</span> (choose the country, then type the number), and <span class="step-title">Location</span> (drag the map pin), then <span class="step-title">Save changes</span>.</li>
```

**Artist Schedule / Quick Reminder section** — change:

```html
<li>Click the <span class="step-title">Quick Reminder</span> button (message icon, top right) to send a one-off SMS to a typed-in name and phone number — no appointment or client record required.</li>
```

to:

```html
<li>Click the <span class="step-title">Quick Reminder</span> button (message icon, top right) to send a one-off SMS to a typed-in name and phone number (choose the country, then type the number) — no appointment or client record required.</li>
```

---

## Section 9 — Architecture docs

Add a new row at the end of the `## Decisions Log` table in `docs/claude/architecture.md`
(directly before the `---` that precedes `## Issuer QA Pass — 2026-07-01 (reconstructed
2026-07-20)`):

```markdown
| Country-code phone inputs — shared `PhoneInput` component (2026-08-26) | New `frontend/src/shared/components/ui/phone-input.tsx` (country-code `Select` + national-number `Input`, backed by `libphonenumber-js/min` for country/calling-code metadata, `AsYouType` formatting, and validity checks) replaces the plain `<Input type="tel">` on the three real phone-entry surfaces in the app: `CreateClientPage.tsx` (`Client.Phone`), `StudioProfilePage.tsx` (`Studio.PhoneNumber`), and `ReminderDialog.tsx`'s raw-contact path (`CreateManualReminderCommand.RecipientPhone`) — confirmed by reading every `phone`/`Phone` reference in `frontend/src/features/**`; everything else is a display-only surface. The component always emits a single E.164 string (no DB schema change — `Client.Phone`/`ManualReminder.RecipientPhone` are `varchar(20)`, already wide enough; `Studio.PhoneNumber` is `longtext`), and derives its initial country from an existing E.164 value or falls back to showing pre-existing legacy freeform data verbatim (not discarded) when it can't parse. Default country is Portugal (`PT`) — matches every pre-existing phone placeholder/test fixture in the codebase, not independently chosen. Backend gained a matching `Matches(E164Format)` FluentValidation rule (`^\+[1-9]\d{1,14}$`, the canonical ITU E.164 shape) in `CreateClientValidator`, `CreateManualReminderValidator`, and — this one previously had **no** phone rule at all — `UpdateMyStudioCommand.cs`'s `UpdateMyStudioValidator`. The regex is duplicated across the three validator classes rather than factored into a shared constant, mirroring this codebase's existing `NiptFormat` duplication between `RegisterStudioValidator` and `UpdateMyStudioValidator`. Motivating bug, not just cosmetics: `NotificationService.SendSmsAsync` passes the stored phone straight into Twilio's `PhoneNumber`, which requires E.164 — before this change nothing guaranteed that shape anywhere in the write path. New dependency: `libphonenumber-js` (`/min` entry point) — version resolved: `<RECORD ACTUAL RESOLVED VERSION HERE>`. | Current vertical-booking-SaaS standard (CLAUDE.md rule 6) — Fresha/Vagaro/Boulevard/GlossGenius-tier booking forms all use a country-code phone picker, not a bare text field, precisely because their SMS reminder pipelines (this app's own `NotificationService`/Twilio integration included) depend on E.164 input. No new UI-combobox dependency for the ~240-country dropdown — Radix `Select`'s built-in typeahead was judged sufficient, consistent with this codebase's "use the shadcn/Radix primitive before reaching for something heavier" convention. Verified: `dotnet build`/`dotnet test`, `pnpm tsc --noEmit`, `pnpm lint`, `pnpm test --run` all green — `<RECORD ACTUAL TEST COUNTS HERE>`. |
```

Fill in the two `<RECORD ...>` placeholders with the real resolved package version and test
counts before ending the session — do not leave them as literal placeholder text in the
committed file.

---

## Out of Scope — flagged explicitly, not silently dropped

Four related gaps came up while scoping this and are real, but each is a separate, larger
change than "add a country-code picker to the existing phone fields" — named here per
`CLAUDE.md` rule 6/7's "flag the gap explicitly" convention:

1. **A client cannot edit their own phone number.** `MyProfilePage.tsx` (`/clients/me`)
   renders `client.phone` as a read-only `ProfileField` — its only "Edit" form covers date of
   birth/allergies/medical notes (the health profile), not name/email/phone. There is no
   `updateClient` (name/email/phone) mutation in `clientsApi.ts` at all — `Client.Phone` can
   currently only be set once, at creation, by an owner/artist via `CreateClientPage`. This is
   a genuine, pre-existing gap unrelated to country codes specifically (it would exist whether
   phone were free-text or E.164) — worth its own spec (new `UpdateMyClientCommand` or similar,
   plus a real edit form on `MyProfilePage`), not a one-line addition here.

2. **Legacy phone data is not backfilled or migrated.** Existing `Client.Phone` and
   `Studio.PhoneNumber` values already in the database that predate this change and aren't
   valid E.164 are left exactly as they are — no backfill script, no forced re-entry. The new
   backend validators only run on new `Create`/`Update` commands, so old rows keep displaying
   as free text until someone re-saves that record through the new `PhoneInput` (at which point
   the new validation applies). Auto-guessing a country code for old freeform numbers
   server-side would be lossy and risky to do silently in the same change that also changes
   what "valid" means going forward — a real candidate for a follow-up data-quality pass, not
   an assumption to bake in here.

3. **`NotificationService.SendSmsAsync` still has no try/catch around the Twilio call.** This
   prompt makes an invalid "to" number far less likely to reach Twilio in the first place
   (frontend validates before submit, backend validates before persisting), but it does not
   change what happens if Twilio itself rejects a call for some other reason (suspended
   account, unverified trial number, rate limit) — that's still an unhandled exception
   surfacing wherever the Hangfire job that calls it runs. Real hardening candidate, but a
   distinct scope from "add country codes to the input fields."

4. **No locale-aware default country.** `PhoneInput` always defaults to Portugal regardless of
   the studio's own city/country or the visiting browser's locale. A studio actually based
   outside Portugal gets a slightly worse first-click experience (has to change the country
   dropdown once) but loses no functionality — every country is still one click away. Deriving
   a smarter per-studio or per-browser default is a real, separate enhancement, not assumed
   here.

---

## Section 10 — Build checklist

Run all of these before ending the session; every one must be clean:

```bash
# 1. Backend build (new FluentValidation rules)
dotnet build

# 2. Backend tests
dotnet test

# 3. Frontend type check
cd frontend && pnpm tsc --noEmit

# 4. Lint
pnpm lint

# 5. All frontend tests must pass (including every new phoneCountries/phoneValidation/
#    phone-input/CreateClientPage/StudioProfilePage/ReminderDialog test)
pnpm test --run

# 6. Frontend build (confirms libphonenumber-js/min bundles cleanly)
pnpm build
```

---

## Summary of Changes

### New features:
- Shared `PhoneInput` component (country-code select + national number field) on all three
  real phone-entry surfaces: `CreateClientPage`, `StudioProfilePage`, `ReminderDialog`'s
  raw-contact path.
- Every phone field now stores/validates a canonical E.164 string, both client- and
  server-side.
- Backend gains a real format check on `Studio.PhoneNumber` for the first time — previously
  unvalidated in both directions (client-side `max(30)` only, no server-side rule at all).
- New dependency: `libphonenumber-js` (frontend only, `/min` entry point).

### Explicitly out of scope (see "Out of Scope" section above):
- Editing a client's own phone number from `MyProfilePage`.
- Backfilling/migrating pre-existing legacy freeform phone data.
- Error handling around `NotificationService.SendSmsAsync`'s Twilio call.
- Locale-aware default country selection.

### Help sync:
- `helpContent.ts` updated (`owner-clients-add`, `owner-studio-profile`).
- `frontend/public/user-manual/index.html` updated (Add Client wireframe/steps, Studio Profile
  wireframe/steps, artist Quick Reminder step).
- Onboarding tours deliberately not touched — justified in Section 8, no tour targets a form
  field.

---

## Hard Rules Reminder

- Tenant isolation: not applicable — no new query, no `IgnoreQueryFilters()` change.
- RBAC: not applicable — no new endpoint, no new authorization policy; the three existing
  endpoints (`CreateClient`, `UpdateMyStudio`, `CreateManualReminder`) keep their existing
  policies unchanged.
- No PII in logs: not applicable — no new logging added; phone numbers are not logged before
  or after this change.
- No new secrets: not applicable.
- New npm dependency, flagged explicitly per rule 6/7's spirit rather than added silently:
  `libphonenumber-js` (`/min` entry). No new NuGet package — the backend uses a plain
  FluentValidation `Matches()` regex, deliberately not a .NET phone-number library, mirroring
  how `Nipt` validation is already handled by regex rather than a dedicated library.
- No new ORM, no new frontend state library: not applicable.
- Structured logs only: not applicable — no new logging.
- Every user-facing change ships with its Help-sync obligations in the same change (Section 8)
  — done, with the tours no-op case justified rather than skipped.
- Match current industry standards (rule 6): this is precisely a "current vertical-booking-SaaS
  standard" gap-closer — see Section 9's Decisions Log entry.
