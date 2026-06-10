import { Link } from "react-router-dom";
import { ChevronRight, Mail, Phone } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import type { ClientResponse } from "../clientsApi";

interface ClientCardProps {
  client: ClientResponse;
}

function getInitials(firstName: string, lastName: string): string {
  return `${firstName[0] ?? ""}${lastName[0] ?? ""}`.toUpperCase();
}

export function ClientCard({ client }: ClientCardProps) {
  return (
    <Link
      to={`/clients/${client.id}`}
      className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
    >
      <Card className="hover:bg-muted/40 transition-colors">
        <CardContent className="p-4 flex items-start gap-4">
          <Avatar className="shrink-0">
            <AvatarFallback>{getInitials(client.firstName, client.lastName)}</AvatarFallback>
          </Avatar>

          <div className="min-w-0 flex-1 space-y-1">
            <p className="text-sm font-medium leading-none">
              {client.firstName} {client.lastName}
            </p>
            <p className="flex items-center gap-1 text-xs text-muted-foreground">
              <Mail className="h-3 w-3 shrink-0" />
              <span className="truncate">{client.email}</span>
            </p>
            {client.phone && (
              <p className="flex items-center gap-1 text-xs text-muted-foreground">
                <Phone className="h-3 w-3 shrink-0" />
                <span className="truncate">{client.phone}</span>
              </p>
            )}
          </div>

          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground self-center" />
        </CardContent>
      </Card>
    </Link>
  );
}
