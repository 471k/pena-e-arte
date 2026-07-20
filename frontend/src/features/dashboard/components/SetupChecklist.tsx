import { useNavigate } from "react-router-dom";
import { Check, Circle } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetDepositRulesQuery } from "@/features/deposit-rules/depositRulesApi";

interface ChecklistItem {
  label:  string;
  done:   boolean;
  href:   string;
  cta:    string;
  tourId?: string;
}

export function SetupChecklist() {
  const navigate = useNavigate();
  const { data: artists = [] }      = useGetArtistsQuery(undefined);
  const { data: depositRules = [] } = useGetDepositRulesQuery(undefined);

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
