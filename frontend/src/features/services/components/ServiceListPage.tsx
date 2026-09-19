import { useNavigate } from "react-router-dom";
import { ListChecks, Plus } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetServicesQuery } from "../servicesApi";
import { ServiceCard } from "./ServiceCard";

export function ServiceListPage() {
  useDocumentMeta({ title: "Services — TattooOS", canonical: "/services" });

  const navigate = useNavigate();
  const canManage = usePermission(Role.Owner);
  const { data: services, isLoading, isError } = useGetServicesQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <ListChecks className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Services</span>
        </div>
        <div className="flex items-center gap-3">
          {services && (
            <span className="text-xs text-muted-foreground">
              {services.length} service{services.length !== 1 ? "s" : ""}
            </span>
          )}
          {canManage && (
            <Button size="sm" onClick={() => navigate("/services/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              New Service
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-2">
        {isLoading && (
          <div className="space-y-3" aria-label="Loading services">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-14 w-full rounded-lg" />
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load services. Please try again.
          </p>
        )}

        {!isLoading && !isError && services?.length === 0 && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <ListChecks className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium text-foreground">No services yet</p>
              <p className="text-xs text-muted-foreground">
                Add a service so clients can pick it while booking — its duration and deposit
                will be applied automatically.
              </p>
            </div>
            {canManage && (
              <Button size="sm" onClick={() => navigate("/services/new")}>
                Add service
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && services && services.length > 0 && services.map((service) => (
          <ServiceCard key={service.id} service={service} />
        ))}
      </main>
    </div>
  );
}
