import { isValidPhoneNumber } from "libphonenumber-js/min";

// Country-neutral on purpose: the field shows its own country picker and a placeholder example for the
// selected country, so the message must not suggest one country's number format (it used to say +351).
export const PHONE_ERROR_MESSAGE = "Enter a valid phone number for the selected country.";

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
