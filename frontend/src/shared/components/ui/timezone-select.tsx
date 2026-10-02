"use client";
import { useMemo } from "react";
import {
  Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue,
} from "./select";
import {
  buildTimezoneOptions, listTimezoneIds, suggestedTimezoneIds, type TimezoneOption,
} from "@/shared/utils/timezones";
import { cn } from "@/shared/utils/cn";

// ~420 zones, each needing two Intl formatters for its label: built once per page load, on first
// use, instead of on every mount of the picker.
let allOptionsCache: TimezoneOption[] | undefined;
function getAllTimezoneOptions(): TimezoneOption[] {
  allOptionsCache ??= buildTimezoneOptions(listTimezoneIds());
  return allOptionsCache;
}

interface TimezoneSelectProps {
  /** IANA id, or undefined/"" while unset. */
  value: string | undefined;
  onChange: (timezone: string) => void;
  /** The country selected on the form: its zones are listed first. */
  countryCode?: string | null;
  /** Where the visitor's IP resolves to; a zone in the selected country is suggested first. */
  ipCountry?: string | null;
  ipTimezone?: string | null;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
  id?: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}

/**
 * Timezone picker: "(UTC+01:00) Tirane — Central European Time", sorted by offset, with the zones
 * of the selected country listed first and a visible scrollbar for the long tail. Reuses the same
 * Radix Select the currency and country pickers use; typing jumps to the first city starting with
 * the typed letters (each item's text value is its city).
 */
export function TimezoneSelect({
  value, onChange, countryCode, ipCountry, ipTimezone, disabled, placeholder, className, id,
  "aria-invalid": ariaInvalid, "aria-describedby": ariaDescribedBy,
}: TimezoneSelectProps) {
  const all = getAllTimezoneOptions();

  const suggestedIds = useMemo(
    () => suggestedTimezoneIds(countryCode, ipCountry, ipTimezone),
    [countryCode, ipCountry, ipTimezone],
  );

  // Built separately from `all`: the browser's own list holds only canonical zones, so a country's
  // zone can be an alias that the list lacks (for example "Europe/Tirane").
  const suggested = useMemo(() => buildTimezoneOptions(suggestedIds), [suggestedIds]);
  const rest = all.filter((o) => !suggestedIds.includes(o.id));

  // A saved zone that is in neither list (an alias the studio already uses) still has to show,
  // and with the same readable label.
  const knownSelected = [...suggested, ...all].find((o) => o.id === value);
  const extra = value && !knownSelected ? buildTimezoneOptions([value])[0] : undefined;
  const selectedLabel = (knownSelected ?? extra)?.label;

  return (
    <Select value={value ?? ""} onValueChange={(v) => { if (v) onChange(v); }} disabled={disabled}>
      <SelectTrigger
        id={id}
        className={cn("min-h-[44px]", className)}
        aria-invalid={ariaInvalid}
        aria-describedby={ariaDescribedBy}
      >
        <SelectValue placeholder={placeholder ?? "Select a timezone"}>{selectedLabel}</SelectValue>
      </SelectTrigger>
      <SelectContent className="max-h-80" showScrollbar>
        {extra && <SelectItem value={extra.id} textValue={extra.city}>{extra.label}</SelectItem>}
        {suggested.length > 0 && (
          <SelectGroup>
            <SelectLabel>Suggested</SelectLabel>
            {suggested.map((o) => (
              <SelectItem key={o.id} value={o.id} textValue={o.city}>{o.label}</SelectItem>
            ))}
          </SelectGroup>
        )}
        <SelectGroup>
          {suggested.length > 0 && <SelectLabel>All time zones</SelectLabel>}
          {rest.map((o) => (
            <SelectItem key={o.id} value={o.id} textValue={o.city}>{o.label}</SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  );
}
