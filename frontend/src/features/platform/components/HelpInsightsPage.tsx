import { HelpCircle, SearchX } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Badge } from "@/shared/components/ui/badge";
import { useGetHelpSearchInsightsQuery } from "@/features/platform/platformApi";
import type { HelpQueryFrequency } from "@/features/platform/platform.types";

function RoleChips({ roles }: { roles: string[] }) {
  return (
    <div className="flex flex-wrap gap-1">
      {roles.map((role) => (
        <Badge key={role} variant="secondary" className="text-[10px] capitalize">
          {role}
        </Badge>
      ))}
    </div>
  );
}

function QueryTable({ rows, emptyLabel }: { rows: HelpQueryFrequency[]; emptyLabel: string }) {
  if (rows.length === 0) {
    return <p className="text-center text-xs text-muted-foreground py-8">{emptyLabel}</p>;
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b bg-muted/40 text-left text-muted-foreground">
            <th className="px-3 py-2 font-medium">Query</th>
            <th className="px-3 py-2 font-medium">Count</th>
            <th className="px-3 py-2 font-medium">Roles</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.query} className="border-b last:border-b-0">
              <td className="px-3 py-2 font-medium truncate max-w-[240px]">{row.query}</td>
              <td className="px-3 py-2 tabular-nums">{row.count}</td>
              <td className="px-3 py-2"><RoleChips roles={row.rolesAsked} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function HelpInsightsPage() {
  useDocumentMeta({ title: "Help Search Insights — Platform Admin", canonical: "/platform/help-insights" });

  const { data, isLoading, isError, refetch } = useGetHelpSearchInsightsQuery({ days: 30 });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <HelpCircle className="h-5 w-5" aria-hidden="true" />
        <span className="font-semibold tracking-tight">Help Search Insights</span>
        {data && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground font-medium">
            {data.totalSearches} searches
          </span>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-4 space-y-6">
        <p className="text-xs text-muted-foreground">
          What studio users searched for in the in-app Help menu over the last {data?.days ?? 30} days.
          Zero-result queries are the highest-signal list of missing documentation or confusing UX.
        </p>

        {isLoading && (
          <div className="space-y-2">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-8 w-full" />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16" role="alert">
            Failed to load help search insights.{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {!isLoading && !isError && data && (
          <>
            <div className="space-y-3">
              <p className="text-sm font-medium">Top queries</p>
              <QueryTable rows={data.topQueries} emptyLabel="No help searches recorded yet." />
            </div>

            <div className="space-y-3 border-t pt-4">
              <div className="flex items-center gap-1.5">
                <SearchX className="h-4 w-4 text-amber-500 dark:text-amber-400" aria-hidden="true" />
                <p className="text-sm font-medium">Zero-result queries</p>
              </div>
              <QueryTable rows={data.zeroResultQueries} emptyLabel="No zero-result searches — nice." />
            </div>
          </>
        )}
      </main>
    </div>
  );
}
