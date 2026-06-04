import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Loader2, Plus, Search, Users } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetClientsQuery } from "../clientsApi";
import { ClientCard } from "./ClientCard";

export function ClientListPage() {
  const navigate = useNavigate();
  const canCreate = usePermission(Role.Artist);
  const [inputValue, setInputValue] = useState("");
  const [search, setSearch] = useState<string | undefined>(undefined);

  useEffect(() => {
    const id = setTimeout(() => setSearch(inputValue.trim() || undefined), 300);
    return () => clearTimeout(id);
  }, [inputValue]);

  const { data: clients, isLoading, isError } = useGetClientsQuery(search);

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
              <span>{clients.length} client{clients.length !== 1 ? "s" : ""}</span>
            </div>
          )}
          {canCreate && (
            <Button size="sm" onClick={() => navigate("/clients/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              New Client
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
          <Input
            placeholder="Search by name or email…"
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            className="pl-9"
          />
        </div>

        {isLoading && (
          <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading clients…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load clients. Please try again.
          </p>
        )}

        {!isLoading && !isError && clients?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            {search ? `No clients match "${search}".` : "No clients in this studio yet."}
          </p>
        )}

        {!isLoading && !isError && clients && clients.length > 0 && (
          <div className="space-y-2">
            {clients.map((client) => (
              <ClientCard key={client.id} client={client} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
