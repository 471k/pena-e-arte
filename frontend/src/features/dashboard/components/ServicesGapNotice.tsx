import { Link } from "react-router-dom";
import { ListChecks } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { useGetServicesQuery } from "@/features/services/servicesApi";

/**
 * C04, the studio's side. A client books by choosing a service and the session length follows from it, but with
 * no active service the booking form has nothing to offer and asks the client how long their tattoo will take.
 * That is a studio-settings gap, so it is said to the owner rather than pushed onto the client.
 *
 * Shown only once the read has answered with zero active services: nothing while loading, and nothing on a failed
 * read (this is advice, not a status, so it doesn't guess).
 */
export function ServicesGapNotice() {
  const { data: services, isSuccess } = useGetServicesQuery();
  if (!isSuccess || !Array.isArray(services) || services.some((s) => s.isActive)) return null;

  return (
    <Card data-testid="services-gap-notice">
      <CardContent className="p-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-start gap-2 min-w-0">
          <ListChecks className="h-4 w-4 mt-0.5 shrink-0 text-muted-foreground" aria-hidden="true" />
          <p className="text-sm">
            Clients booking online are asked to choose a session length themselves, because you have no active
            services. Add your services so they choose a service and the length follows from it.
          </p>
        </div>
        <Button asChild size="sm" variant="outline">
          <Link to="/services">Add services</Link>
        </Button>
      </CardContent>
    </Card>
  );
}
