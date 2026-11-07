import { memo } from "react";
import { SelectItem } from "./select";
import { PHONE_COUNTRIES, flagEmoji } from "@/shared/utils/phoneCountries";

/**
 * The ~245 countries as Select items ("🇦🇱 Albania"), for the studio country pickers.
 *
 * Memoized with no props, so it renders once per picker and never again when the page around it
 * re-renders. Radix Select keeps every item mounted while the list is closed (that is how the
 * closed trigger's type-ahead, the selected item's text in a bare <SelectValue />, and the hidden
 * native <select> work), so an inline `PHONE_COUNTRIES.map(...)` re-rendered all of them on every
 * keystroke anywhere on the page. Items still re-render when the Select itself changes (value,
 * open/closed), which is what moves the check mark.
 *
 * textValue is the country name: Radix's type-ahead matches the start of an item's text, and the
 * visible text starts with a flag emoji, so without it typing a country's name never matched.
 */
export const CountrySelectItems = memo(function CountrySelectItems() {
  return PHONE_COUNTRIES.map((c) => (
    <SelectItem key={c.code} value={c.code} textValue={c.name}>
      {flagEmoji(c.code)} {c.name}
    </SelectItem>
  ));
});
