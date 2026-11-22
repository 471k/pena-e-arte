import { Fragment, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ScrollText, ChevronDown, ChevronRight, X } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { useGetAuditLogQuery, useGetAuditLogVocabularyQuery } from "@/features/platform/platformApi";
import { useGetStudiosQuery } from "@/features/studios/studiosApi";
import type { AuditLogEntryResponse } from "@/features/platform/platform.types";

export const AUDIT_PAGE_SIZE = 50;

// Every filter and the page live in the URL (D01, D08): a reload, Back or a shared link keeps the
// investigation. All of them are named choices, ids or dates, so nothing private ever reaches the URL.
const FILTER_KEYS = ["action", "targetType", "studioId", "actorUserId", "from", "to"] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

const selectClass = "h-8 w-full rounded-md border border-input bg-background px-2 text-xs";

// Audit times are UTC, stated on the page: the platform spans studios in different zones, and the
// date filters are whole UTC days on the server.
function formatUtc(iso: string): string {
  return `${new Date(iso).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "UTC",
  })} UTC`;
}

function prettyMetadata(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

function hasMetadata(raw: string): boolean {
  const trimmed = raw.trim();
  return trimmed !== "" && trimmed !== "{}";
}

/** "admin-impersonating" reads as a role, not a slug. */
function roleLabel(role: string): string {
  return role === "admin-impersonating" ? "admin (impersonating)" : role;
}

/**
 * Who acted, as the admin read resolved it: a staff member's current account name, "Deleted user" when the account
 * is gone, "No name on file" when there is none. A client is always just "Client": this log is read across every
 * studio, and a client's name is the most sensitive identity that could appear in it.
 */
function actorLabel(row: AuditLogEntryResponse): string {
  if (row.actorRole === "client") return "Client";
  if (row.actorAccountDeleted) return "Deleted user";
  return row.actorName ?? "No name on file";
}

function AuditRowDetail({
  row, onOnlyActor, onOnlyStudio,
}: {
  row: AuditLogEntryResponse;
  onOnlyActor: (actorUserId: string) => void;
  onOnlyStudio: (studioId: string) => void;
}) {
  return (
    <div className="space-y-3 px-3 py-3 bg-muted/20 text-xs">
      <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1.5">
        <dt className="text-muted-foreground">Action</dt>
        <dd className="font-medium">{row.action}</dd>
        <dt className="text-muted-foreground">Target</dt>
        <dd>{row.targetType === "Platform" ? "Platform (no single target)" : <>{row.targetType} · <span className="font-mono break-all">{row.targetId}</span></>}</dd>
        <dt className="text-muted-foreground">Studio</dt>
        <dd>{row.studioId ? <>{row.studioName ?? "Unknown studio"} · <span className="font-mono break-all">{row.studioId}</span></> : "Platform-wide"}</dd>
        <dt className="text-muted-foreground">Actor</dt>
        <dd>{actorLabel(row)} · <span className="capitalize">{roleLabel(row.actorRole)}</span> · <span className="font-mono break-all">{row.actorUserId}</span></dd>
        <dt className="text-muted-foreground">When</dt>
        <dd>{formatUtc(row.createdAt)}</dd>
      </dl>
      <div>
        <p className="text-muted-foreground mb-1">Details recorded</p>
        {hasMetadata(row.metadata)
          ? <pre className="whitespace-pre-wrap break-all rounded border bg-background p-2 font-mono text-[11px]">{prettyMetadata(row.metadata)}</pre>
          : <p>None recorded for this action.</p>}
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="button" size="sm" variant="outline" className="h-7 text-xs" onClick={() => onOnlyActor(row.actorUserId)}>
          Only this actor's actions
        </Button>
        {row.studioId && (
          <Button type="button" size="sm" variant="outline" className="h-7 text-xs" onClick={() => onOnlyStudio(row.studioId!)}>
            Only this studio
          </Button>
        )}
      </div>
    </div>
  );
}

function AuditLogTable({
  rows, onOnlyActor, onOnlyStudio,
}: {
  rows: AuditLogEntryResponse[];
  onOnlyActor: (actorUserId: string) => void;
  onOnlyStudio: (studioId: string) => void;
}) {
  const [openId, setOpenId] = useState<string | null>(null);

  if (rows.length === 0) {
    return <p className="text-center text-xs text-muted-foreground py-12">No audit log entries match these filters.</p>;
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b bg-muted/40 text-left text-muted-foreground">
            <th className="px-3 py-2 font-medium"><span className="sr-only">Details</span></th>
            <th className="px-3 py-2 font-medium">When (UTC)</th>
            <th className="px-3 py-2 font-medium">Action</th>
            <th className="px-3 py-2 font-medium">Target</th>
            <th className="px-3 py-2 font-medium">Studio</th>
            <th className="px-3 py-2 font-medium">Actor</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const open = openId === row.id;
            return (
              <Fragment key={row.id}>
                <tr className="border-b last:border-b-0">
                  <td className="px-1 py-1">
                    <button
                      type="button"
                      aria-expanded={open}
                      aria-controls={`audit-detail-${row.id}`}
                      onClick={() => setOpenId(open ? null : row.id)}
                      className="inline-flex h-8 w-8 items-center justify-center rounded hover:bg-muted"
                    >
                      {open ? <ChevronDown className="h-4 w-4" aria-hidden="true" /> : <ChevronRight className="h-4 w-4" aria-hidden="true" />}
                      <span className="sr-only">{open ? "Hide details" : "Show details"} for {row.action}</span>
                    </button>
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap">{formatUtc(row.createdAt)}</td>
                  <td className="px-3 py-2 font-medium">{row.action}</td>
                  <td className="px-3 py-2 text-muted-foreground">
                    {/* Platform-wide actions (e.g. the subscription backfills) have no single target, so
                        their id is an all-zero placeholder — show the type alone rather than "00000000". */}
                    {row.targetType === "Platform" ? (
                      "Platform"
                    ) : (
                      <>{row.targetType} · <span className="font-mono text-[10px]">{row.targetId.slice(0, 8)}</span></>
                    )}
                  </td>
                  <td className="px-3 py-2 text-muted-foreground">
                    {row.studioId
                      ? (row.studioName ?? <span className="font-mono text-[10px]">{row.studioId.slice(0, 8)}</span>)
                      : "Platform-wide"}
                  </td>
                  <td className="px-3 py-2 text-muted-foreground">
                    {actorLabel(row)} <span className="capitalize">· {roleLabel(row.actorRole)}</span>
                  </td>
                </tr>
                {open && (
                  <tr id={`audit-detail-${row.id}`} className="border-b last:border-b-0">
                    <td colSpan={6} className="p-0">
                      <AuditRowDetail row={row} onOnlyActor={onOnlyActor} onOnlyStudio={onOnlyStudio} />
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export function AuditLogPage() {
  useDocumentMeta({ title: "Audit Log — Platform Admin", canonical: "/platform/audit-log" });

  const [params, setParams] = useSearchParams();
  const filter = (key: FilterKey): string => params.get(key) ?? "";
  const page = Math.max(1, Number.parseInt(params.get("page") ?? "1", 10) || 1);

  // A filter change goes back to page 1; replace keeps Back meaningful (one entry per investigation).
  function setFilter(key: FilterKey, value: string) {
    setParams((prev) => {
      const next = new URLSearchParams(prev);
      if (value) next.set(key, value); else next.delete(key);
      next.delete("page");
      return next;
    }, { replace: true });
  }

  function setPage(nextPage: number) {
    setParams((prev) => {
      const next = new URLSearchParams(prev);
      if (nextPage > 1) next.set("page", String(nextPage)); else next.delete("page");
      return next;
    });
  }

  const anyFilter = FILTER_KEYS.some((key) => params.has(key));

  const { data: vocabulary } = useGetAuditLogVocabularyQuery();
  const { data: studios } = useGetStudiosQuery();
  const studioOptions = [...(studios ?? [])].sort((a, b) => a.name.localeCompare(b.name));

  const { data, isLoading, isFetching, isError, refetch } = useGetAuditLogQuery({
    action:      filter("action") || undefined,
    targetType:  filter("targetType") || undefined,
    studioId:    filter("studioId") || undefined,
    actorUserId: filter("actorUserId") || undefined,
    from:        filter("from") || undefined,
    to:          filter("to") || undefined,
    page,
    pageSize:    AUDIT_PAGE_SIZE,
  });

  const pageCount = data ? Math.max(1, Math.ceil(data.totalCount / AUDIT_PAGE_SIZE)) : 1;
  const actorFilter = filter("actorUserId");

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--admin-nav-height)] z-10">
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
          platform, most recent first. Times are in UTC, and the From and To dates are whole UTC days,
          both included.
        </p>

        <div className="grid grid-cols-2 page-md:grid-cols-3 gap-3">
          <div className="space-y-1">
            <Label htmlFor="filter-action" className="text-xs">Action</Label>
            <select id="filter-action" value={filter("action")} onChange={(e) => setFilter("action", e.target.value)} className={selectClass}>
              <option value="">All actions</option>
              {vocabulary?.actions.map((a) => <option key={a} value={a}>{a}</option>)}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="filter-target-type" className="text-xs">Target type</Label>
            <select id="filter-target-type" value={filter("targetType")} onChange={(e) => setFilter("targetType", e.target.value)} className={selectClass}>
              <option value="">All target types</option>
              {vocabulary?.targetTypes.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="filter-studio" className="text-xs">Studio</Label>
            <select id="filter-studio" value={filter("studioId")} onChange={(e) => setFilter("studioId", e.target.value)} className={selectClass}>
              <option value="">All studios</option>
              {studioOptions.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="filter-from" className="text-xs">From (UTC day)</Label>
            <Input id="filter-from" type="date" value={filter("from")} onChange={(e) => setFilter("from", e.target.value)} className="h-8 text-xs" />
          </div>
          <div className="space-y-1">
            <Label htmlFor="filter-to" className="text-xs">To (UTC day, included)</Label>
            <Input id="filter-to" type="date" value={filter("to")} onChange={(e) => setFilter("to", e.target.value)} className="h-8 text-xs" />
          </div>
          <div className="flex items-end">
            <Button type="button" variant="outline" size="sm" className="h-8 text-xs w-full"
              disabled={!anyFilter} onClick={() => setParams(new URLSearchParams(), { replace: true })}>
              Clear filters
            </Button>
          </div>
        </div>

        {actorFilter && (
          <p className="flex items-center gap-2 text-xs">
            <span>Showing one actor's actions: <span className="font-mono">{actorFilter.slice(0, 8)}</span></span>
            <button type="button" onClick={() => setFilter("actorUserId", "")}
              className="inline-flex items-center gap-1 rounded border px-1.5 py-0.5 hover:bg-muted">
              <X className="h-3 w-3" aria-hidden="true" /> Show all actors
            </button>
          </p>
        )}

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

        {!isLoading && !isError && data && (
          <>
            <AuditLogTable
              key={`${page}-${params.toString()}`}
              rows={data.items}
              onOnlyActor={(id) => setFilter("actorUserId", id)}
              onOnlyStudio={(id) => setFilter("studioId", id)}
            />
            {data.totalCount > AUDIT_PAGE_SIZE && (
              <nav aria-label="Audit log pages" className="flex items-center justify-between gap-2 text-xs">
                <Button type="button" variant="outline" size="sm" className="h-8 text-xs"
                  disabled={page <= 1 || isFetching} onClick={() => setPage(page - 1)}>
                  Newer
                </Button>
                <span aria-live="polite">Page {page} of {pageCount}</span>
                <Button type="button" variant="outline" size="sm" className="h-8 text-xs"
                  disabled={page >= pageCount || isFetching} onClick={() => setPage(page + 1)}>
                  Older
                </Button>
              </nav>
            )}
          </>
        )}
      </main>
    </div>
  );
}
