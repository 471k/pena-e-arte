"use client";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "./select";
import { buildCurrencyOptions } from "@/shared/utils/currencies";
import { currencyLabel } from "@/shared/utils/formatCurrency";
import { cn } from "@/shared/utils/cn";

interface CurrencySelectProps {
  value: string | null;
  onChange: (code: string) => void;
  /** Preselected/shown-first entry — the studio's country's default currency, or null if the
   * server couldn't resolve one (unknown country). */
  countryDefault?: string | null;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
  id?: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}

/**
 * Currency picker for registration and Studio Settings. Reuses the same Radix Select primitive
 * PhoneInput's country picker uses (this codebase has no separate cmdk-style search combobox) —
 * Radix Select's built-in type-ahead (typing jumps to the first matching item) makes a ~150-entry
 * list navigable without a dedicated search box.
 */
export function CurrencySelect({
  value, onChange, countryDefault = null, disabled, placeholder, className, id,
  "aria-invalid": ariaInvalid, "aria-describedby": ariaDescribedBy,
}: CurrencySelectProps) {
  const options = buildCurrencyOptions(countryDefault);

  return (
    <Select value={value ?? ""} onValueChange={onChange} disabled={disabled}>
      <SelectTrigger
        id={id}
        className={cn("min-h-[44px]", className)}
        aria-invalid={ariaInvalid}
        aria-describedby={ariaDescribedBy}
      >
        <SelectValue placeholder={placeholder ?? "Select a currency"}>
          {value ? `${currencyLabel(value)} — ${value}` : undefined}
        </SelectValue>
      </SelectTrigger>
      <SelectContent className="max-h-72" showScrollbar>
        {options.map((o) => (
          <SelectItem key={o.code} value={o.code}>
            {o.name} ({o.code})
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
