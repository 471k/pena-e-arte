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
