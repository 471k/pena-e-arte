import { useState } from "react";
import { ScrollText } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { useGetAuditLogQuery } from "@/features/platform/platformApi";
import type { AuditLogEntryResponse } from "@/features/platform/platform.types";

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function AuditLogTable({ rows }: { rows: AuditLogEntryResponse[] }) {
  if (rows.length === 0) {
    return <p className="text-center text-xs text-muted-foreground py-12">No audit log entries match these filters.</p>;
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b bg-muted/40 text-left text-muted-foreground">
            <th className="px-3 py-2 font-medium">When</th>
            <th className="px-3 py-2 font-medium">Action</th>
            <th className="px-3 py-2 font-medium">Target</th>
            <th className="px-3 py-2 font-medium">Studio</th>
            <th className="px-3 py-2 font-medium">Actor role</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} className="border-b last:border-b-0">
              <td className="px-3 py-2 whitespace-nowrap">{formatDateTime(row.createdAt)}</td>
              <td className="px-3 py-2 font-medium">{row.action}</td>
              <td className="px-3 py-2 text-muted-foreground">
                {row.targetType} · <span className="font-mono text-[10px]">{row.targetId.slice(0, 8)}</span>
              </td>
              <td className="px-3 py-2 text-muted-foreground">
                {row.studioId ? <span className="font-mono text-[10px]">{row.studioId.slice(0, 8)}</span> : "Platform-wide"}
              </td>
              <td className="px-3 py-2 capitalize text-muted-foreground">{row.actorRole}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function AuditLogPage() {
  useDocumentMeta({ title: "Audit Log — Platform Admin", canonical: "/platform/audit-log" });

  const [action, setAction] = useState("");
  const [targetType, setTargetType] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const { data, isLoading, isError, refetch } = useGetAuditLogQuery({
    action: action || undefined,
    targetType: targetType || undefined,
    from: from || undefined,
    to: to || undefined,
    pageSize: 50,
  });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <ScrollText className="h-5 w-5" aria-hidden="true" />
        <span className="font-semibold tracking-tight">Audit Log</span>
        {data && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground font-medium">
            {data.totalCount}
          </span>
        )}
      </header>

      <main className="max-w-4xl mx-auto px-4 py-4 space-y-4">
        <p className="text-xs text-muted-foreground">
          Every suspend, cancel, plan-edit, and other trust-sensitive action taken across the
          platform, most recent first.
        </p>

        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
          <div className="space-y-1">
            <Label htmlFor="filter-action" className="text-xs">Action</Label>
            <Input id="filter-action" value={action} onChange={(e) => setAction(e.target.value)}
              placeholder="e.g. Studio.Suspended" className="h-8 text-xs" />
          </div>
          <div className="space-y-1">
            <Label htmlFor="filter-target-type" className="text-xs">Target type</Label>
            <Input id="filter-target-type" value={targetType} onChange={(e) => setTargetType(e.target.value)}
              placeholder="e.g. Studio" className="h-8 text-xs" />
          </div>
          <div className="space-y-1">
            <Label htmlFor="filter-from" className="text-xs">From</Label>
            <Input id="filter-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-8 text-xs" />
          </div>
          <div className="space-y-1">
            <Label htmlFor="filter-to" className="text-xs">To</Label>
            <Input id="filter-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-8 text-xs" />
          </div>
        </div>

        {isLoading && (
          <div className="space-y-2">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-8 w-full" />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16" role="alert">
            Failed to load the audit log.{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {!isLoading && !isError && data && <AuditLogTable rows={data.items} />}
      </main>
    </div>
  );
}
