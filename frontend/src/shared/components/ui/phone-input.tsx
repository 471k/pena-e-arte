"use client";
import { useState } from "react";
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
  const [country, setCountry] = useState<PhoneCountryCode>(DEFAULT_PHONE_COUNTRY);
  const [nationalText, setNationalText] = useState("");
  // Tracks the last value this component itself emitted, so an external change to `value`
  // (e.g. RHF's `reset()` after the parent form's data loads asynchronously) can be told
  // apart from this component's own keystroke bouncing back through the parent — otherwise
  // every keystroke's own onChange would immediately fight the user's typing. Plain state,
  // not a ref: mutating a ref during render is unsafe under the React Compiler, and this
  // "compare against a state-tracked previous value, setState conditionally during render"
  // shape is React's own documented pattern for adjusting state from a changed prop. Seeded
  // as undefined, never from `value` itself — seeding from the live value would make the
  // very first external value look "already applied" and skip deriving country/nationalText
  // from it on mount (the exact class of sentinel bug flagged in this codebase's own
  // state-sync feedback notes).
  const [lastEmitted, setLastEmitted] = useState<string | undefined>(undefined);

  if (value !== lastEmitted) {
    setLastEmitted(value);
    const derived = deriveState(value);
    setCountry(derived.country);
    setNationalText(derived.nationalText);
  }

  function emit(next: string) {
    setLastEmitted(next);
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
