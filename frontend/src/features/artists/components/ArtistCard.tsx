import { Link } from "react-router-dom";
import { ChevronRight, Mail, Tag } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { TATTOO_STYLE_OPTIONS } from "@/shared/constants/tattooStyles";
import type { ArtistResponse } from "../artistsApi";

interface ArtistCardProps {
  artist: ArtistResponse;
}

function getInitials(firstName: string, lastName: string): string {
  return `${firstName[0] ?? ""}${lastName[0] ?? ""}`.toUpperCase();
}

function specializationLabel(style: string): string {
  return TATTOO_STYLE_OPTIONS.find((o) => o.value === style)?.label ?? style;
}

export function ArtistCard({ artist }: ArtistCardProps) {
  return (
    <Link to={`/artists/${artist.id}`} className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg">
      <Card className="hover:bg-muted/40 transition-colors">
        <CardContent className="p-4 flex items-start gap-4">
          <Avatar className="shrink-0">
            <AvatarFallback>{getInitials(artist.firstName, artist.lastName)}</AvatarFallback>
          </Avatar>

          <div className="min-w-0 flex-1 space-y-1">
            <p className="text-sm font-medium leading-none">
              {artist.firstName} {artist.lastName}
            </p>
            <p className="flex items-center gap-1 text-xs text-muted-foreground">
              <Mail className="h-3 w-3 shrink-0" />
              <span className="truncate">{artist.email}</span>
            </p>
            {artist.specializations.length > 0 && (
              <p className="flex items-center gap-1 text-xs text-muted-foreground">
                <Tag className="h-3 w-3 shrink-0" />
                <span className="truncate">{artist.specializations.map(specializationLabel).join(", ")}</span>
              </p>
            )}
          </div>

          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground self-center" />
        </CardContent>
      </Card>
    </Link>
  );
}
