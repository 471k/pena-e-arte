import { ScrollText } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetMyStudioAuditLogQuery } from "../studiosApi";

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

export function StudioAuditLogCard() {
  const { data, isLoading, isError } = useGetMyStudioAuditLogQuery({ pageSize: 10 });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base flex items-center gap-2">
          <ScrollText className="h-4 w-4" />
          Recent studio activity
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isLoading && (
          <div className="space-y-2">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-6 w-full" />)}
          </div>
        )}

        {isError && (
          <p className="text-sm text-destructive-text">Failed to load recent activity.</p>
        )}

        {!isLoading && !isError && data && data.items.length === 0 && (
          <p className="text-sm text-muted-foreground">No recorded actions yet.</p>
        )}

        {!isLoading && !isError && data && data.items.length > 0 && (
          <ul className="space-y-2">
            {data.items.map((item) => (
              <li key={item.id} className="flex items-center justify-between gap-3 text-xs">
                <span className="font-medium">{item.action}</span>
                <span className="text-muted-foreground whitespace-nowrap">{formatDateTime(item.createdAt)}</span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
