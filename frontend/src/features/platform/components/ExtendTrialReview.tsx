import { useState } from "react";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { extractErrorMessage } from "@/shared/utils/extractErrorMessage";
import { useExtendTrialMutation } from "@/features/platform/platformApi";
import { ConsequenceReview, ReviewAlert } from "./ConsequenceReview";
import { AUDIT_ACTIONS, makeReceipt, type ActionReceipt } from "./receipts";
import { DAY_MS, extendedTrialEnd, formatUtc } from "./subscriptionConsequences";

/** A 422 is ExtendTrialHandler's business rule refusing (days are checked before sending, so not the validator). */
function isRuleRefusal(err: unknown): boolean {
  return typeof err === "object" && err !== null && "status" in err && (err as { status: unknown }).status === 422;
}

interface ExtendTrialReviewProps {
  studioId:        string;
  studioName:      string;
  idSuffix:        string;
  /** The subscription status as the page knows it ("NoSubscription" when there is no subscription row). */
  status:          string;
  /** The trial end the page shows: the subscription's, else the studio's. */
  currentTrialEnd: string | null;
  /** Called once the server confirmed the extension, with its receipt (D04). */
  onDone:          (receipt: ActionReceipt) => void;
  onBack:          () => void;
}

/** Extend or re-grant a studio's trial (D03), with what it will do stated before it is confirmed. */
export function ExtendTrialReview({
  studioId, studioName, idSuffix, status, currentTrialEnd, onDone, onBack,
}: ExtendTrialReviewProps) {
  const [days,    setDays]    = useState("7");
  const [failure, setFailure] = useState<{ message: string; attempt: number; refused: boolean } | null>(null);
  const [extendTrial, { isLoading }] = useExtendTrialMutation();

  const id      = `extend-${idSuffix}`;
  const parsed  = Number.parseInt(days, 10);
  const valid   = Number.isInteger(parsed) && parsed >= 1 && parsed <= 90; // ExtendTrialValidator
  const now     = new Date();
  const expired = currentTrialEnd ? new Date(currentTrialEnd) <= now : true;
  const newEnd  = valid ? extendedTrialEnd(currentTrialEnd, parsed, now) : null;

  async function submit() {
    if (!valid) return;
    try {
      await extendTrial({ studioId, additionalDays: parsed }).unwrap();
      onDone(makeReceipt(
        `Trial extended by ${parsed} day${parsed === 1 ? "" : "s"} for ${studioName}${newEnd ? `: it now ends ${formatUtc(newEnd)}` : ""}.`,
        AUDIT_ACTIONS.trialExtended, studioId,
      ));
    } catch (err) {
      setFailure((prev) => ({
        message: extractErrorMessage(err, "The trial wasn't extended."),
        attempt: (prev?.attempt ?? 0) + 1,
        refused: isRuleRefusal(err),
      }));
    }
  }

  const happens = !newEnd
    ? ["Enter between 1 and 90 days to see what extending will do."]
    : [
        `The trial ends ${formatUtc(newEnd)}${currentTrialEnd ? ` instead of ${formatUtc(currentTrialEnd)}` : ""}${
          expired ? ", counted from now because the trial has already ended" : ""}.`,
        ...(status !== "NoSubscription"
          ? [`The grace period after it moves too: it ends 7 days later, ${formatUtc(new Date(newEnd.getTime() + 7 * DAY_MS))}.`]
          : []),
        ...(status === "GracePeriod" ? ["The studio goes back to Trialing and can make changes again."] : []),
        ...(status === "Trialing" && expired ? ["The studio can use TattooOS again until then."] : []),
      ];

  return (
    <ConsequenceReview
      id={id}
      title={expired ? "Grant trial extension" : "Extend trial"}
      studioName={studioName}
      happens={happens}
      permanent={["The days can't be taken back: a trial can't be shortened from here."]}
      alert={failure ? (
        <ReviewAlert key={failure.attempt}>
          {failure.refused ? (
            // The server's rule said no (e.g. the studio became past due after this page loaded). Its
            // message says what to do instead; trying again would only be refused again.
            <p className="font-medium">The trial wasn't extended: {failure.message}</p>
          ) : (
            <>
              <p className="font-medium">Extension failed: {failure.message}</p>
              <p>Your number of days is kept, so you can try again.</p>
            </>
          )}
        </ReviewAlert>
      ) : null}
      confirmLabel={failure && !failure.refused ? "Try again" : "Extend trial"}
      busy={isLoading}
      confirmDisabled={!valid || (failure?.refused ?? false)}
      onConfirm={() => void submit()}
      onBack={onBack}
    >
      <div className="flex items-center gap-2">
        <Label htmlFor={`${id}-days`} className="text-xs text-muted-foreground">
          {expired ? "Grant" : "Extend by"}
        </Label>
        <Input
          id={`${id}-days`}
          type="number" min="1" max="90"
          value={days}
          onChange={(e) => setDays(e.target.value)}
          className="h-7 w-20 text-xs"
        />
        <span className="text-xs text-muted-foreground">days</span>
      </div>
    </ConsequenceReview>
  );
}
