import { useParams, useNavigate } from "react-router-dom";
import { Loader2, ExternalLink } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useGetSharedDesignQuery } from "../publicApi";

export function SharedDesignPage() {
  const { token = "" }  = useParams<{ token: string }>();
  const navigate         = useNavigate();
  const { data: design, isLoading, isError } = useGetSharedDesignQuery(token, { skip: !token });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-black">
        <Loader2 className="h-6 w-6 animate-spin text-white/50" />
      </div>
    );
  }

  if (isError || !design) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-4 bg-background px-4">
        <p className="text-lg font-medium">This link has expired</p>
        <p className="text-sm text-muted-foreground text-center">
          The design share link is no longer available or has been revoked.
        </p>
        <Button variant="outline" onClick={() => navigate("/")}>
          Go home
        </Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-black flex flex-col">
      <div className="flex-1 flex items-center justify-center p-4">
        <img
          src={design.imageUrl}
          alt={design.title}
          className="max-h-[80vh] max-w-full object-contain rounded"
        />
      </div>

      <div className="bg-background border-t p-4">
        <div className="max-w-2xl mx-auto space-y-3">
          <div>
            <h1 className="text-lg font-semibold">{design.title}</h1>
            <p className="text-sm text-muted-foreground">
              By {design.studioName} · Expires {new Date(design.expiresAt).toLocaleDateString()}
            </p>
          </div>
          <Button
            className="w-full"
            onClick={() => navigate(`/s/${design.studioSlug}`)}
          >
            <ExternalLink className="h-4 w-4 mr-2" />
            Book your own tattoo
          </Button>
        </div>
      </div>
    </div>
  );
}
