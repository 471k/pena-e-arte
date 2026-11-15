import { BodyMap } from "@/features/clients/components/BodyMap";

interface DesiredPlacementFieldProps {
  locations: string[];
  onChange:  (locations: string[]) => void;
}

/** BodyMap reused for "where do you want this tattoo", scoped to this booking rather than the client's
 *  tattoo history (Decision #7). It names its own group (C10): a fieldset whose legend is "Desired
 *  placement" — the old label pointed at an id nothing carried. */
export function DesiredPlacementField({ locations, onChange }: DesiredPlacementFieldProps) {
  return <BodyMap locations={locations} onChange={onChange} legend="Desired placement" perspective="self" />;
}
