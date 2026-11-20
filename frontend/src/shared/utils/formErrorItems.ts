import type { FieldErrors, FieldValues } from "react-hook-form";
import type { FormErrorItem } from "@/shared/components/FormErrorSummary";

/**
 * Error-summary items for a react-hook-form's errors, in `order` (the order the fields appear on the page).
 * The field's element id must equal its name, as it does in the booking forms; `labels` gives each its visible name.
 */
export function rhfErrorItems<T extends FieldValues>(
  errors: FieldErrors<T>,
  order: { name: keyof T & string; label: string; fieldId?: string }[],
): FormErrorItem[] {
  const items: FormErrorItem[] = [];
  for (const { name, label, fieldId } of order) {
    const message = errors[name]?.message;
    if (typeof message === "string" && message) items.push({ fieldId: fieldId ?? name, label, message });
  }
  return items;
}

/** An item for an error kept outside the form library (intake answers, images), when there is one. */
export function extraErrorItem(fieldId: string, label: string, message: string | null | undefined): FormErrorItem[] {
  return message ? [{ fieldId, label, message }] : [];
}
