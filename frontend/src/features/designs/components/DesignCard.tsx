import { ChevronRight, Palette, Upload } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { usePermission } from "@/shared/hooks/usePermission";
import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import type { DesignResponse } from "../design.types";
import { waitingLabel, type DesignViewer } from "../designWaiting";
import { DesignStatusBadge } from "./DesignStatusBadge";

interface DesignCardProps {
  design: DesignResponse;
}

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day:   "numeric",
    month: "short",
    year:  "numeric",
  });
}

export function DesignCard({ design }: DesignCardProps) {
  const navigate   = useNavigate();
  const canUpload  = usePermission(Role.Artist);
  const role       = useAppSelector((s) => s.auth.role);
  const viewer: DesignViewer = role === Role.Client ? "client" : role === Role.Artist ? "artist" : "owner";
  // E04c (A05): which queue the design is in, with the version it is about.
  const waiting = design.isCatalogItem ? null : waitingLabel(design.waitingOn, viewer);

  return (
    <Card className="hover:bg-muted/40 transition-colors">
      <CardContent className="p-4 flex items-center gap-4">
        <Link
          to={`/designs/${design.id}`}
          className="flex items-center gap-4 flex-1 min-w-0 focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded"
        >
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-muted">
            <Palette className="h-5 w-5 text-muted-foreground" />
          </div>

          <div className="min-w-0 flex-1 space-y-1">
            <div className="flex items-center gap-2">
              <p className="text-sm font-medium leading-none truncate">{design.title}</p>
              <DesignStatusBadge status={design.status} />
            </div>
            {design.description && (
              <p className="text-xs text-muted-foreground truncate">{design.description}</p>
            )}
            <p className="text-xs text-muted-foreground">
              {formatDate(design.createdAt)}
              {design.latestVersionNumber != null && <> · Latest: v{design.latestVersionNumber}</>}
              {waiting && <> · <span data-testid="design-waiting-label" className="font-medium text-foreground">{waiting}</span></>}
            </p>
          </div>

          <ChevronRight className="h-4 w-4 text-muted-foreground/40 shrink-0 self-center" />
        </Link>

        {canUpload && (
          <Button
            variant="ghost"
            size="sm"
            className="shrink-0 h-8 w-8 p-0"
            aria-label="Upload revision"
            onClick={() => navigate(`/designs/${design.id}/upload`)}
          >
            <Upload className="h-4 w-4" />
          </Button>
        )}
      </CardContent>
    </Card>
  );
}
