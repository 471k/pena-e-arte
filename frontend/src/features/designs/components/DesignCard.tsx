import { Palette } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import type { DesignResponse } from "../design.types";

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
  return (
    <Card>
      <CardContent className="p-4 flex items-start gap-4">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-muted">
          <Palette className="h-5 w-5 text-muted-foreground" />
        </div>

        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-sm font-medium leading-none">{design.title}</p>
          {design.description && (
            <p className="text-xs text-muted-foreground truncate">{design.description}</p>
          )}
          <p className="text-xs text-muted-foreground">{formatDate(design.createdAt)}</p>
        </div>
      </CardContent>
    </Card>
  );
}
