import type { FieldValues, Path, UseFormRegister } from "react-hook-form";
import type { ServiceRequirementFlags } from "../service.types";

const REQUIREMENTS: { key: keyof ServiceRequirementFlags; label: string; hint: string }[] = [
  { key: "requiresDepositPaid",    label: "Deposit paid",            hint: "Met when the deposit is paid by card or you confirm the cash." },
  { key: "requiresIntakeForm",     label: "Intake form submitted",   hint: "Met when the client submits an intake form for this appointment." },
  { key: "requiresConsentForm",    label: "Consent form signed",     hint: "Met when the client signs the consent form for this appointment." },
  { key: "requiresReferenceImage", label: "Reference image added",   hint: "Met when the client attaches a reference image while booking." },
];

/**
 * DEC03: what a client should do before an appointment for this service. Each switch only adds an item
 * to the client's checklist on their appointment page; whether it is met is worked out from what the
 * client has actually done, and it never blocks booking, confirming or completing.
 */
export function ServiceRequirementsFieldset<T extends FieldValues & ServiceRequirementFlags>({
  register, idPrefix,
}: {
  register: UseFormRegister<T>;
  idPrefix: string;
}) {
  return (
    <fieldset className="space-y-2">
      <legend className="text-sm font-medium">Client checklist (optional)</legend>
      <p className="text-xs text-muted-foreground">
        Clients booking this service see these as things to do on their appointment page. It never
        blocks a booking: you can still confirm and complete an appointment with items missing.
      </p>
      {REQUIREMENTS.map(({ key, label, hint }) => {
        const id = `${idPrefix}-${key}`;
        return (
          <div key={key} className="flex items-start gap-2">
            <input
              type="checkbox"
              id={id}
              aria-describedby={`${id}-hint`}
              {...register(key as Path<T>)}
              className="mt-0.5 h-4 w-4 rounded border-input accent-primary"
            />
            <div>
              <label htmlFor={id} className="text-sm cursor-pointer">{label}</label>
              <p id={`${id}-hint`} className="text-xs text-muted-foreground">{hint}</p>
            </div>
          </div>
        );
      })}
    </fieldset>
  );
}
