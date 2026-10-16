import { useEffect } from "react";
import { Link } from "react-router-dom";
import { SearchX } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

interface NotFoundPageProps {
  homePath:      string;
  homeLabel:     string;
  /** Guests also get a way into the public studio directory. */
  showDiscover?: boolean;
}

// Shown for any URL no route matches. It used to redirect silently (guests to /discover, signed-in
// users to their home), which hid broken links and sent people somewhere they never asked for.
export function NotFoundPage({ homePath, homeLabel, showDiscover = false }: NotFoundPageProps) {
  useEffect(() => {
    const previous = document.title;
    document.title = "Page not found — TattooOS";
    return () => { document.title = previous; };
  }, []);

  return (
    <div className="min-h-screen bg-background flex items-center justify-center px-4">
      <main className="max-w-md w-full space-y-4 text-center">
        <SearchX className="h-10 w-10 mx-auto text-muted-foreground" aria-hidden="true" />
        <h1 className="text-xl font-semibold tracking-tight">Page not found</h1>
        <p className="text-sm text-muted-foreground">
          The page you&apos;re looking for doesn&apos;t exist or has moved. Check the link, or carry on from here.
        </p>
        <div className="flex flex-col sm:flex-row gap-2 justify-center pt-2">
          <Button asChild className="bg-violet-600 hover:bg-violet-700 text-white">
            <Link to={homePath}>{homeLabel}</Link>
          </Button>
          {showDiscover && (
            <Button asChild variant="outline">
              <Link to="/discover">Discover studios</Link>
            </Button>
          )}
        </div>
      </main>
    </div>
  );
}
