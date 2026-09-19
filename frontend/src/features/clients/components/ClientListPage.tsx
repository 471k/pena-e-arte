import { useEffect, useState } from "react";
import { toast } from "sonner";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useNavigate } from "react-router-dom";
import { Archive, ArchiveRestore, ChevronRight, Download, MoreVertical, Plus, Search, Users } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { DataTable } from "@/shared/components/DataTable";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/shared/components/ui/dropdown-menu";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useAppSelector } from "@/app/hooks";
import { downloadAuthenticatedFile } from "@/shared/utils/downloadAuthenticatedFile";
import { useGetClientsQuery, useArchiveClientMutation, useRestoreClientMutation } from "../clientsApi";
import type { ClientResponse } from "../clientsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";

function ClientRowSkeleton() {
  return (
    <div
      className="flex items-center gap-3 px-3 py-3 border-b"
      aria-hidden="true"
    >
      <Skeleton className="h-7 w-7 rounded-full shrink-0" />
      <div className="flex-1 space-y-1.5">
        <Skeleton className="h-3.5 w-28" />
        <Skeleton className="h-3 w-40 opacity-60" />
      </div>
      <Skeleton className="h-3.5 w-28" />
      <Skeleton className="h-7 w-14 rounded-md" />
    </div>
  );
}

export function ClientListPage() {
  useDocumentMeta({ title: "Clients — TattooOS", canonical: "/clients" });

  const navigate  = useNavigate();
  const canCreate = usePermission(Role.Artist);
  const canExport = usePermission(Role.Owner);
  const { token, tenantId } = useAppSelector((s) => s.auth);
  const [inputValue, setInputValue] = useState("");
  const [search, setSearch]         = useState<string | undefined>(undefined);
  const [artistFilter, setArtistFilter] = useState<string>("all");
  const [exporting, setExporting] = useState(false);
  const [showArchived, setShowArchived] = useState(false);
  const [archiveTarget, setArchiveTarget] = useState<ClientResponse | null>(null);
  const [restoreTarget, setRestoreTarget] = useState<ClientResponse | null>(null);
  const [archiveClient, { isLoading: isArchiving }] = useArchiveClientMutation();
  const [restoreClient, { isLoading: isRestoring }] = useRestoreClientMutation();

  async function handleArchive() {
    if (!archiveTarget) return;
    const result = await archiveClient(archiveTarget.id);
    if ("error" in result) {
      toast.error("Couldn't archive this client. Please try again.");
    } else {
      toast.success(`${archiveTarget.firstName} ${archiveTarget.lastName} archived.`);
    }
    setArchiveTarget(null);
  }

  async function handleRestore() {
    if (!restoreTarget) return;
    const result = await restoreClient(restoreTarget.id);
    if ("error" in result) {
      toast.error("Couldn't restore this client. Please try again.");
    } else {
      toast.success(`${restoreTarget.firstName} ${restoreTarget.lastName} restored.`);
    }
    setRestoreTarget(null);
  }

  async function handleExportCsv() {
    setExporting(true);
    try {
      await downloadAuthenticatedFile("clients/export.csv", "clients.csv", token, tenantId);
    } catch {
      toast.error("Couldn't export clients. Please try again.");
    } finally {
      setExporting(false);
    }
  }

  useEffect(() => {
    const id = setTimeout(() => setSearch(inputValue.trim() || undefined), 300);
    return () => clearTimeout(id);
  }, [inputValue]);

  const { data: clients, isLoading, isError } = useGetClientsQuery({ search, includeArchived: showArchived });
  const { data: artists } = useGetArtistsQuery(undefined);
  const errorMessage = useSuspensionAwareError(isError, "Failed to load clients. Please try again.");

  const filteredClients = (clients ?? []).filter((c) => {
    if (artistFilter === "all") return true;
    if (artistFilter === "unassigned") return c.artistId === null;
    return c.artistId === artistFilter;
  });

  const isFiltered = !!search || artistFilter !== "all";
  const hasAnyClients = (clients?.length ?? 0) > 0;

  const selectedArtistName = artists?.find((a) => a.id === artistFilter);
  const emptyMessage = search
    ? `No clients match "${search}".`
    : artistFilter === "unassigned"
      ? "No unassigned clients."
      : selectedArtistName
        ? `No clients assigned to ${selectedArtistName.firstName} ${selectedArtistName.lastName}.`
        : "No clients in this studio yet.";

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <Users className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Clients</span>
        </div>
        <div className="flex items-center gap-3">
          {clients && (
            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <Users className="h-3.5 w-3.5" />
              <span>{filteredClients.length} client{filteredClients.length !== 1 ? "s" : ""}</span>
            </div>
          )}
          {canExport && (isLoading || hasAnyClients || isFiltered) && (
            <Button
              variant="outline"
              size="sm"
              onClick={handleExportCsv}
              disabled={exporting}
              className="gap-1.5"
            >
              <Download className="h-3.5 w-3.5" />
              Export CSV
            </Button>
          )}
          {canCreate && (isLoading || hasAnyClients || isFiltered) && (
            <Button size="sm" onClick={() => navigate("/clients/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              New Client
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        <div className="flex gap-2">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
            <Input
              placeholder="Search by name or email…"
              value={inputValue}
              onChange={(e) => setInputValue(e.target.value)}
              className="pl-9"
            />
          </div>
          <Select value={artistFilter} onValueChange={setArtistFilter}>
            <SelectTrigger className="w-[180px]" aria-label="Filter by artist">
              <SelectValue placeholder="All artists" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All artists</SelectItem>
              <SelectItem value="unassigned">Unassigned</SelectItem>
              {artists?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.firstName} {a.lastName}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <label className="flex items-center gap-2 text-xs text-muted-foreground shrink-0 pl-1">
            <ToggleSwitch
              checked={showArchived}
              onChange={() => setShowArchived((v) => !v)}
              aria-label="Show archived clients"
            />
            Show archived
          </label>
        </div>

        {isLoading && (
          <div className="space-y-0">
            {Array.from({ length: 8 }).map((_, i) => (
              <ClientRowSkeleton key={i} />
            ))}
          </div>
        )}

        {errorMessage && (
          <p className="text-center text-sm text-destructive-text py-16" role="alert">
            {errorMessage}
          </p>
        )}

        {/* Rich empty state — zero clients, no filter active */}
        {!isLoading && !isError && !hasAnyClients && !isFiltered && (
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <Users className="h-8 w-8 text-muted-foreground/40" />
            <p className="text-sm font-medium">No clients yet</p>
            <p className="text-xs text-muted-foreground">
              Add your first client to get started.
            </p>
            {canCreate && (
              <Button
                size="sm"
                onClick={() => navigate("/clients/new")}
                className="gap-1.5 mt-1"
              >
                <Plus className="h-3.5 w-3.5" />
                New Client
              </Button>
            )}
          </div>
        )}

        {/* Table — when clients exist or a filter is active */}
        {!isLoading && !isError && (hasAnyClients || isFiltered) && (
          <DataTable<ClientResponse>
            columns={[
              {
                header: "Name",
                cell: (c) => (
                  <div className={`flex items-center gap-2 ${c.archivedAt ? "opacity-60" : ""}`}>
                    <div className="h-7 w-7 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0 select-none">
                      {c.firstName[0]?.toUpperCase()}{c.lastName[0]?.toUpperCase()}
                    </div>
                    <span className="font-medium">{c.firstName} {c.lastName}</span>
                    {c.archivedAt && (
                      <span className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-muted text-muted-foreground">
                        Archived
                      </span>
                    )}
                  </div>
                ),
              },
              { header: "Email", accessorKey: "email" },
              {
                header: "Phone",
                cell: (c) =>
                  c.phone ?? (
                    <span aria-label="Not provided" className="text-muted-foreground">
                      —
                    </span>
                  ),
              },
              {
                header: "Artist",
                cell: (c) =>
                  c.artistName ?? (
                    <span aria-label="Unassigned" className="text-muted-foreground">
                      —
                    </span>
                  ),
              },
              {
                header: "",
                cell: (c) => (
                  <div
                    className="flex items-center justify-end gap-1"
                    onClick={(e) => e.stopPropagation()}
                  >
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-7 text-xs gap-1 text-muted-foreground hover:text-foreground"
                      onClick={() => navigate(`/clients/${c.id}`)}
                    >
                      View
                      <ChevronRight className="h-3 w-3" />
                    </Button>
                    {canCreate && (
                      <DropdownMenu modal={false}>
                        <DropdownMenuTrigger asChild>
                          <Button
                            size="icon"
                            variant="ghost"
                            className="h-7 w-7"
                            aria-label={`More options for ${c.firstName} ${c.lastName}`}
                          >
                            <MoreVertical className="h-3.5 w-3.5" aria-hidden />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {c.archivedAt ? (
                            <DropdownMenuItem
                              onClick={() => setRestoreTarget(c)}
                              className="flex items-center gap-2"
                            >
                              <ArchiveRestore className="h-4 w-4" aria-hidden />
                              Restore
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem
                              onClick={() => setArchiveTarget(c)}
                              className="flex items-center gap-2"
                            >
                              <Archive className="h-4 w-4" aria-hidden />
                              Archive
                            </DropdownMenuItem>
                          )}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    )}
                  </div>
                ),
              },
            ]}
            data={filteredClients}
            keyExtractor={(c) => c.id}
            onRowClick={(c) => navigate(`/clients/${c.id}`)}
            emptyMessage={emptyMessage}
            mobileCard={(c) => (
              <div className={`flex items-center gap-2 ${c.archivedAt ? "opacity-60" : ""}`}>
                <div className="h-8 w-8 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0 select-none">
                  {c.firstName[0]?.toUpperCase()}{c.lastName[0]?.toUpperCase()}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="font-medium truncate">
                    {c.firstName} {c.lastName}
                    {c.archivedAt && (
                      <span className="ml-1.5 text-xs font-normal text-muted-foreground">(Archived)</span>
                    )}
                  </p>
                  <p className="text-xs text-muted-foreground truncate">
                    {c.email}{c.phone ? ` · ${c.phone}` : ""} · {c.artistName ?? "Unassigned"}
                  </p>
                </div>
                <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
              </div>
            )}
          />
        )}
      </main>

      <AlertDialog
        open={archiveTarget !== null}
        onOpenChange={(open) => !open && setArchiveTarget(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Archive {archiveTarget?.firstName} {archiveTarget?.lastName}?
            </AlertDialogTitle>
            <AlertDialogDescription>
              They&apos;ll be removed from your active client list. Their appointments, payments,
              and consent records stay exactly as they are, and you can restore them any time.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isArchiving}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleArchive} disabled={isArchiving}>
              Archive
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog
        open={restoreTarget !== null}
        onOpenChange={(open) => !open && setRestoreTarget(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Restore {restoreTarget?.firstName} {restoreTarget?.lastName}?
            </AlertDialogTitle>
            <AlertDialogDescription>
              They&apos;ll reappear in your active client list.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isRestoring}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleRestore} disabled={isRestoring}>
              Restore
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
