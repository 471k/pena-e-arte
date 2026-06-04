import { Link } from "react-router-dom";
import { ChevronRight, Mail, Tag } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import type { ArtistResponse } from "../artistsApi";

interface ArtistCardProps {
  artist: ArtistResponse;
}

function getInitials(firstName: string, lastName: string): string {
  return `${firstName[0] ?? ""}${lastName[0] ?? ""}`.toUpperCase();
}

export function ArtistCard({ artist }: ArtistCardProps) {
  return (
    <Link to={`/artists/${artist.id}`} className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg">
      <Card className="hover:bg-muted/40 transition-colors">
        <CardContent className="p-4 flex items-start gap-4">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-muted text-sm font-semibold text-muted-foreground select-none">
            {getInitials(artist.firstName, artist.lastName)}
          </div>

          <div className="min-w-0 flex-1 space-y-1">
            <p className="text-sm font-medium leading-none">
              {artist.firstName} {artist.lastName}
            </p>
            <p className="flex items-center gap-1 text-xs text-muted-foreground">
              <Mail className="h-3 w-3 shrink-0" />
              <span className="truncate">{artist.email}</span>
            </p>
            {artist.specializations && (
              <p className="flex items-center gap-1 text-xs text-muted-foreground">
                <Tag className="h-3 w-3 shrink-0" />
                <span className="truncate">{artist.specializations}</span>
              </p>
            )}
          </div>

          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground self-center" />
        </CardContent>
      </Card>
    </Link>
  );
}
