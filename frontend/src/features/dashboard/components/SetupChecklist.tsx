import { useNavigate } from "react-router-dom";
import { Check, Circle } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetDepositRulesQuery } from "@/features/deposit-rules/depositRulesApi";
import { useGetMyStudioQuery, useGetStudioHoursQuery } from "@/features/studios/studiosApi";

interface ChecklistItem {
  label:  string;
  done:   boolean;
  href:   string;
  cta:    string;
  tourId?: string;
}

export function SetupChecklist() {
  const navigate = useNavigate();
  const { data: artists = [], isLoading: artistsLoading }           = useGetArtistsQuery(undefined);
  const { data: depositRules = [], isLoading: depositRulesLoading } = useGetDepositRulesQuery(undefined);
  const { data: studio, isLoading: studioLoading }                  = useGetMyStudioQuery();
  const { data: hours = [], isLoading: hoursLoading } =
    useGetStudioHoursQuery(studio?.id ?? "", { skip: !studio?.id });

  // All queries default to [] while their first request is in flight, which would
  // otherwise render "0/3 complete" for a moment on every dashboard load — even for a
  // fully-set-up studio — before the real data arrives. Wait for all to resolve at
  // least once rather than flash a false "incomplete" state.
  if (artistsLoading || depositRulesLoading || studioLoading || (studio?.id && hoursLoading)) return null;

  const items: ChecklistItem[] = [
    {
      label: "Add your first artist",
      done:  artists.length > 0,
      href:  "/artists/new",
      cta:   "Add artist",
    },
    {
      label:  "Set a deposit rule",
      done:   depositRules.length > 0,
      href:   "/deposit-rules/new",
      cta:    "Set rule",
      tourId: "owner-deposit-rules-nav",
    },
    {
      // Simplification, explicitly noted: this tracks "hours rows exist," not "the owner
      // has ever saved the hours form" — RegisterStudioHandler seeds a default Mon–Fri
      // schedule at registration, so this item is done immediately for every new studio.
      // There's no existing precedent elsewhere in this file for the stronger
      // "has ever saved" distinction, so this mirrors the simpler rows-exist check the
      // other two items already use.
      label:  "Set your hours",
      done:   hours.length > 0,
      href:   "/studios/me",
      cta:    "Set hours",
      tourId: "owner-studio-profile-nav",
    },
  ];

  const doneCount = items.filter((i) => i.done).length;

  if (doneCount === items.length) return null;

  return (
    <Card data-testid="setup-checklist">
      <CardContent className="p-4 space-y-3">
        <div className="flex items-center justify-between">
          <span className="text-sm font-medium">Studio setup</span>
          <span className="text-xs text-muted-foreground">
            {doneCount}/{items.length} complete
          </span>
        </div>
        <div className="space-y-2">
          {items.map((item) => (
            <div
              key={item.label}
              className="flex items-center justify-between gap-3"
            >
              <div className="flex items-center gap-2">
                {item.done ? (
                  <Check
                    className="h-4 w-4 text-emerald-500 shrink-0"
                    aria-hidden="true"
                  />
                ) : (
                  <Circle
                    className="h-4 w-4 text-muted-foreground shrink-0"
                    aria-hidden="true"
                  />
                )}
                <span
                  className={
                    item.done
                      ? "text-sm text-muted-foreground line-through"
                      : "text-sm"
                  }
                >
                  {item.label}
                </span>
              </div>
              {!item.done && (
                <Button
                  size="sm"
                  variant="outline"
                  className="h-6 text-xs px-2 shrink-0"
                  onClick={() => navigate(item.href)}
                  data-tour={item.tourId}
                >
                  {item.cta}
                </Button>
              )}
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}
