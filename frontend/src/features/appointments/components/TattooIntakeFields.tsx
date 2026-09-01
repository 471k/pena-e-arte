import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { Input } from "@/shared/components/ui/input";
import { cn } from "@/shared/utils/cn";
import { ReferralSource } from "../appointment.types";
import { FieldLabel } from "./FieldLabel";
import type { TattooIntakeValues } from "./tattooIntakeValidation";

const REFERRAL_SOURCE_OPTIONS: { value: string; label: string }[] = [
  { value: ReferralSource.Instagram,        label: "Instagram" },
  { value: ReferralSource.TikTok,           label: "TikTok" },
  { value: ReferralSource.YouTube,          label: "YouTube" },
  { value: ReferralSource.FriendsAndFamily, label: "Friends & family" },
  { value: ReferralSource.Other,            label: "Somewhere else" },
];

interface TattooIntakeFieldsProps {
  value:                     TattooIntakeValues;
  onChange:                  (value: TattooIntakeValues) => void;
  tattooDescriptionError?:   string;
  referralSourceOtherError?: string;
}

/** Tattoo description + "how did you hear about us" + safety notes — shared by the
 *  authenticated BookAppointmentForm and the guest checkout form (Decision #8). */
export function TattooIntakeFields({
  value,
  onChange,
  tattooDescriptionError,
  referralSourceOtherError,
}: TattooIntakeFieldsProps) {
  return (
    <>
      <div className="space-y-1.5">
        <FieldLabel htmlFor="tattooDescription" required>
          What are you looking to get done?
        </FieldLabel>
        <Textarea
          id="tattooDescription"
          rows={3}
          placeholder="Style, size, placement, references you have in mind…"
          value={value.tattooDescription}
          onChange={(e) => onChange({ ...value, tattooDescription: e.target.value })}
          className={cn("resize-none", tattooDescriptionError && "border-destructive")}
        />
        {tattooDescriptionError && (
          <p className="text-xs text-destructive" role="alert">{tattooDescriptionError}</p>
        )}
      </div>

      <div className="space-y-1.5">
        <FieldLabel htmlFor="referralSource">How did you hear about us?</FieldLabel>
        <Select
          value={value.referralSource}
          onValueChange={(v) => onChange({ ...value, referralSource: v })}
        >
          <SelectTrigger id="referralSource">
            <SelectValue placeholder="Select an option" />
          </SelectTrigger>
          <SelectContent>
            {REFERRAL_SOURCE_OPTIONS.map(({ value: v, label }) => (
              <SelectItem key={v} value={v}>{label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {value.referralSource === ReferralSource.Other && (
          <div className="pt-1.5">
            <Input
              aria-label="Tell us where you heard about us"
              placeholder="Tell us where…"
              value={value.referralSourceOther}
              onChange={(e) => onChange({ ...value, referralSourceOther: e.target.value })}
              className={cn(referralSourceOtherError && "border-destructive")}
            />
            {referralSourceOtherError && (
              <p className="text-xs text-destructive mt-1" role="alert">{referralSourceOtherError}</p>
            )}
          </div>
        )}
      </div>

      <div className="space-y-1.5">
        <FieldLabel htmlFor="safetyNotes">Anything else I should know?</FieldLabel>
        <Textarea
          id="safetyNotes"
          rows={2}
          placeholder="Medical conditions, allergies, medications, skin concerns…"
          value={value.safetyNotes}
          onChange={(e) => onChange({ ...value, safetyNotes: e.target.value })}
          className="resize-none"
        />
      </div>
    </>
  );
}
