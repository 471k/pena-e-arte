import { Link } from "react-router-dom";
import { ChevronRight, Clock, ListChecks } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import { formatCurrency } from "@/shared/utils/formatCurrency";
import { useStudioCurrency } from "@/shared/hooks/useStudioCurrency";
import type { ServiceResponse } from "../service.types";

interface ServiceCardProps {
  service: ServiceResponse;
}

export function ServiceCard({ service }: ServiceCardProps) {
  const { currency } = useStudioCurrency();
  return (
    <Link
      to={`/services/${service.id}`}
      className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
    >
      <Card className="hover:bg-muted/40 transition-colors">
        <CardContent className="p-4 flex items-center gap-4">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-blue-500/10 text-blue-600">
            <ListChecks className="h-4 w-4" />
          </div>

          <div className="min-w-0 flex-1 space-y-1">
            <p className="text-sm font-medium leading-none">{service.name}</p>
            <p className="text-xs text-muted-foreground flex items-center gap-1">
              <Clock className="h-3 w-3" />
              {service.durationMinutes} min
              {service.price !== null && currency && ` · from ${formatCurrency(service.price, currency)}`}
              {service.depositAmount !== null && currency && ` · ${formatCurrency(service.depositAmount, currency)} deposit`}
            </p>
          </div>

          <div className="flex items-center gap-2">
            <span className={cn(
              "text-xs px-2 py-0.5 rounded-full font-medium",
              service.isActive
                ? "bg-green-500/10 text-green-700"
                : "bg-muted text-muted-foreground",
            )}>
              {service.isActive ? "Active" : "Inactive"}
            </span>
            <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
          </div>
        </CardContent>
      </Card>
    </Link>
  );
}
