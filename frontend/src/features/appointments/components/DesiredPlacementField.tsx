import { BodyMap } from "@/features/clients/components/BodyMap";
import { FieldLabel } from "./FieldLabel";

interface DesiredPlacementFieldProps {
  locations: string[];
  onChange:  (locations: string[]) => void;
}

/** Thin wrapper around the existing, unmodified BodyMap picker — reused as-is for "where do
 *  you want this tattoo," scoped to this booking rather than the client's tattoo history
 *  (Decision #7). */
export function DesiredPlacementField({ locations, onChange }: DesiredPlacementFieldProps) {
  return (
    <div className="space-y-1.5">
      <FieldLabel htmlFor="desiredPlacement">Desired placement</FieldLabel>
      <BodyMap locations={locations} onChange={onChange} />
    </div>
  );
}
