import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { formatSessionLength } from "../sessionLength";
import { FieldLabel } from "./FieldLabel";

/** The client's choice: a service (which carries the length), or "something else" with a length they pick. */
export interface ServiceChoice {
  serviceId: string | null;
  custom:    boolean;
}

const CUSTOM = "custom";

/** What the picker reads: satisfied by both the staff ServiceResponse and the public booking page's service. */
export interface ServiceOption {
  id:              string;
  name:            string;
  description:     string | null;
  durationMinutes: number;
}

/**
 * C04: the client chooses a SERVICE and the session length follows from it. The picker used to start on
 * "Custom (choose your own duration)", so unless the client went looking, the form asked them how long a tattoo
 * takes. Now it starts unchosen ("Choose a service") when `requireChoice` is on, and "Something else" is the last
 * option rather than the default. Staff booking for a client keep the old default (`requireChoice` off): they
 * know the length of work that isn't on the list.
 */
export function ServiceChoiceSelect({
  services,
  choice,
  requireChoice,
  error,
  onChange,
}: {
  services:      ServiceOption[];
  choice:        ServiceChoice;
  requireChoice: boolean;
  error:         string | null;
  onChange:      (choice: ServiceChoice) => void;
}) {
  const value = choice.serviceId ?? (choice.custom || !requireChoice ? CUSTOM : "");
  const selected = services.find((s) => s.id === choice.serviceId) ?? null;

  return (
    <div className="space-y-1.5">
      <FieldLabel htmlFor="serviceId" required={requireChoice}>Service</FieldLabel>
      <Select
        value={value}
        onValueChange={(v) => onChange(v === CUSTOM ? { serviceId: null, custom: true } : { serviceId: v, custom: false })}
      >
        <SelectTrigger
          id="serviceId"
          aria-invalid={error ? true : undefined}
          aria-describedby="serviceId-hint"
        >
          <SelectValue placeholder="Choose a service" />
        </SelectTrigger>
        <SelectContent>
          {services.map((s) => (
            <SelectItem key={s.id} value={s.id}>
              {s.name} — {formatSessionLength(s.durationMinutes)}
            </SelectItem>
          ))}
          <SelectItem value={CUSTOM}>Something else (choose a session length)</SelectItem>
        </SelectContent>
      </Select>
      <p id="serviceId-hint" className="text-xs text-muted-foreground">
        {selected?.description ?? "The service you choose sets how long your session is."}
      </p>
      {error && <p className="text-xs text-destructive-text" role="alert">{error}</p>}
    </div>
  );
}
