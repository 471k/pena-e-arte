"use client";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "./select";
import { buildCurrencyOptions } from "@/shared/utils/currencies";
import { currencyLabel } from "@/shared/utils/formatCurrency";
import { cn } from "@/shared/utils/cn";

interface CurrencySelectProps {
  value: string | null;
  onChange: (code: string) => void;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
  id?: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}

/**
 * Currency picker for registration and Studio Settings: Albanian Lek (ALL) first, then Euro (EUR)
 * — the only two a studio may choose (buildCurrencyOptions). Reuses the same Radix Select
 * primitive PhoneInput's country picker uses. The country's default is preselected by the form
 * (from the server), not by this component.
 */
export function CurrencySelect({
  value, onChange, disabled, placeholder, className, id,
  "aria-invalid": ariaInvalid, "aria-describedby": ariaDescribedBy,
}: CurrencySelectProps) {
  const options = buildCurrencyOptions(value);

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
      <SelectContent>
        {options.map((o) => (
          <SelectItem key={o.code} value={o.code}>
            {o.name} ({o.code})
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
