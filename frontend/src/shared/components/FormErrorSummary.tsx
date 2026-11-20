import { useEffect, useRef } from "react";
import { AlertCircle } from "lucide-react";

export interface FormErrorItem {
  /** The id of the control to focus: the input itself, never its wrapper. */
  fieldId: string;
  /** The field's name as the person sees it. */
  label:   string;
  message: string;
}

/**
 * S04 (slice 7 phase B): every error in a long form, listed at its top, each linking to and focusing its field.
 * Covers the errors outside the form library too (intake answers, images), which is the half S04 names.
 * It receives focus once per failed submit, so a keyboard or screen-reader user starts from the list.
 *
 * It shows field names and validation messages only, never what was entered, and logs nothing.
 */
export function FormErrorSummary({ items, attempt }: { items: FormErrorItem[]; attempt: number }) {
  const ref = useRef<HTMLDivElement>(null);
  const focusedFor = useRef(0);

  useEffect(() => {
    if (items.length > 0 && attempt > focusedFor.current) {
      focusedFor.current = attempt;
      ref.current?.focus();
    }
  }, [attempt, items.length]);

  if (items.length === 0) return null;

  function focusField(fieldId: string) {
    const target = document.getElementById(fieldId);
    if (!target) return;
    target.focus();
    target.scrollIntoView?.({ block: "center" });
  }

  return (
    <div
      ref={ref}
      tabIndex={-1}
      role="alert"
      aria-labelledby="form-error-summary-title"
      className="space-y-2 rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      <p id="form-error-summary-title" className="flex items-center gap-1.5 font-medium text-destructive-text">
        <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
        {items.length === 1 ? "There's 1 thing to fix" : `There are ${items.length} things to fix`}
      </p>
      <ul className="list-disc space-y-1 pl-5">
        {items.map((item) => (
          <li key={item.fieldId}>
            <a
              href={`#${item.fieldId}`}
              className="text-destructive-text underline underline-offset-2"
              onClick={(e) => { e.preventDefault(); focusField(item.fieldId); }}
            >
              {item.label}: {item.message}
            </a>
          </li>
        ))}
      </ul>
    </div>
  );
}
