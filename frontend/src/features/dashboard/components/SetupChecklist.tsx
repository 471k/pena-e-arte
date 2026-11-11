import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Check, Circle, CircleDashed } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import {
  useConfirmStudioHoursMutation,
  useGetSetupReadinessQuery,
  type SetupAreaKey,
  type SetupAreaState,
} from "@/features/studios/studiosApi";

/**
 * The owner's setup checklist (E05a, O02). Each area is one of three states from the server:
 * not configured, set for you but not yet reviewed, or reviewed. It used to count rows, which called
 * every new studio's hours done because registration seeds a Mon–Fri schedule. Only "Reviewed" counts
 * as complete, so a studio with nothing but seeded rows is never shown as set up.
 */

interface AreaCopy {
  label: string;
  href:  string;
  cta:   string;
  tourId?: string;
  /** For an area that can be "set for you, not yet reviewed": what to tell the owner, and the button to check it. */
  unreviewed?: { note: string; cta: string; canConfirm: boolean };
}

const COPY: Record<SetupAreaKey, AreaCopy> = {
  Artists:      { label: "Add your first artist", href: "/artists/new", cta: "Add artist" },
  DepositRules: { label: "Set a deposit rule",    href: "/deposit-rules/new", cta: "Set rule", tourId: "owner-deposit-rules-nav" },
  Hours: {
    label: "Set your hours", href: "/studios/me", cta: "Set hours", tourId: "owner-studio-profile-nav",
    unreviewed: { note: "We've set opening hours for you. Check they're right.", cta: "Check hours", canConfirm: true },
  },
  // A07a. No "They're right" here: every studio starts on commission with no rate, which can't be
  // confirmed (the server refuses terms that give artists no share), so the owner sets it in the form.
  PayModel: {
    label: "Set how artists get paid", href: "/studios/me", cta: "Set pay model",
    unreviewed: {
      note: "Every studio starts on commission with no rate set, so artists can't see their share yet.",
      cta: "Set pay model",
      canConfirm: false,
    },
  },
};

function StateIcon({ state }: { state: SetupAreaState }) {
  if (state === "Reviewed") {
    return <Check className="h-4 w-4 text-success-text shrink-0" aria-hidden="true" />;
  }
  if (state === "DefaultUnreviewed") {
    return <CircleDashed className="h-4 w-4 text-warning-text shrink-0" aria-hidden="true" />;
  }
  return <Circle className="h-4 w-4 text-muted-foreground shrink-0" aria-hidden="true" />;
}

export function SetupChecklist() {
  const navigate = useNavigate();
  // Refetch on every dashboard visit: adding an artist or a deposit rule happens in other API slices,
  // whose cache tags can't reach this one.
  const { data, isLoading, isError, refetch } =
    useGetSetupReadinessQuery(undefined, { refetchOnMountOrArgChange: true });
  const [confirmHours, { isLoading: confirming }] = useConfirmStudioHoursMutation();

  // Nothing until the answer arrives: a loading checklist would flash "0/3" at a set-up studio.
  if (isLoading) return null;

  // A response without an areas list (a broken proxy, a test's catch-all mock) is treated like a
  // failed read: never as "nothing left to set up".
  if (isError || !data || !Array.isArray(data.areas)) {
    // Never fall back to "complete": say we couldn't check, and let the owner retry.
    return (
      <Card data-testid="setup-checklist">
        <CardContent className="p-4 flex items-center justify-between gap-3">
          <p className="text-sm text-muted-foreground" role="alert">
            We couldn't check your studio setup just now.
          </p>
          <Button size="sm" variant="outline" className="h-6 text-xs px-2 shrink-0" onClick={() => void refetch()}>
            Try again
          </Button>
        </CardContent>
      </Card>
    );
  }

  const doneCount = data.areas.filter((a) => a.state === "Reviewed").length;
  if (doneCount === data.areas.length) return null;

  async function onConfirmHours() {
    try {
      await confirmHours().unwrap();
      toast.success("Opening hours confirmed");
    } catch {
      toast.error("Couldn't confirm your hours. Please try again.");
    }
  }

  return (
    <Card data-testid="setup-checklist">
      <CardContent className="p-4 space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium">Studio setup</h2>
          <span className="text-xs text-muted-foreground">
            {doneCount}/{data.areas.length} complete
          </span>
        </div>
        <ul className="space-y-2">
          {data.areas.map((area) => {
            const copy = COPY[area.area];
            const reviewed = area.state === "Reviewed";
            const unreviewedDefault = area.state === "DefaultUnreviewed";
            return (
              <li
                key={area.area}
                className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1"
                data-testid={`setup-area-${area.area}`}
                data-state={area.state}
              >
                <div className="flex items-start gap-2 min-w-0">
                  <span className="mt-0.5"><StateIcon state={area.state} /></span>
                  <div className="min-w-0">
                    <span className={reviewed ? "text-sm text-muted-foreground line-through" : "text-sm"}>
                      {copy.label}
                    </span>
                    {unreviewedDefault && copy.unreviewed && (
                      <p className="text-xs text-muted-foreground">{copy.unreviewed.note}</p>
                    )}
                    <span className="sr-only">
                      {reviewed ? " (done)" : unreviewedDefault ? " (set for you, not yet reviewed)" : " (not set up yet)"}
                    </span>
                  </div>
                </div>
                {!reviewed && (
                  <div className="flex gap-2 shrink-0">
                    {unreviewedDefault && copy.unreviewed?.canConfirm && (
                      <Button
                        size="sm"
                        variant="outline"
                        className="h-6 text-xs px-2"
                        disabled={confirming}
                        onClick={() => void onConfirmHours()}
                      >
                        They're right
                      </Button>
                    )}
                    <Button
                      size="sm"
                      variant="outline"
                      className="h-6 text-xs px-2"
                      onClick={() => navigate(copy.href)}
                      data-tour={copy.tourId}
                    >
                      {unreviewedDefault && copy.unreviewed ? copy.unreviewed.cta : copy.cta}
                    </Button>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      </CardContent>
    </Card>
  );
}
