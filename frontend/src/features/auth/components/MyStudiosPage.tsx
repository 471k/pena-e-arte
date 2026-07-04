import { useState } from "react";
import { Building2, CheckCircle2, ExternalLink, Loader2 } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { setCredentials } from "@/features/auth/authSlice";
import { decodeToken } from "@/shared/utils/jwt";
import { useGetMyStudiosQuery, useSwitchStudioMutation } from "@/features/auth/authApi";
import type { MyStudioResponse } from "@/features/auth/authApi";

// ── Helpers ───────────────────────────────────────────────────────────────────

function StudioAvatar({ name, coverImageUrl }: { name: string; coverImageUrl: string | null }) {
  if (coverImageUrl) {
    return (
      <img
        src={coverImageUrl}
        alt={name}
        className="h-12 w-12 rounded-md object-cover shrink-0"
      />
    );
  }

  const initials = name
    .split(" ")
    .map((w) => w[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);

  return (
    <div className="h-12 w-12 rounded-md bg-primary/10 text-primary flex items-center justify-center text-sm font-semibold shrink-0">
      {initials}
    </div>
  );
}

// ── Studio card ───────────────────────────────────────────────────────────────

interface StudioCardProps {
  studio:      MyStudioResponse;
  isActive:    boolean;
  isSwitching: boolean;
  onSwitch:    (studioId: string) => void;
}

function StudioCard({ studio, isActive, isSwitching, onSwitch }: StudioCardProps) {
  return (
    <Card
      className={`transition-colors ${
        isActive ? "ring-2 ring-primary ring-offset-2 ring-offset-background" : ""
      }`}
    >
      <CardContent className="p-4">
        <div className="flex items-start gap-3">
          <StudioAvatar name={studio.name} coverImageUrl={studio.coverImageUrl} />

          <div className="flex-1 min-w-0 space-y-0.5">
            <div className="flex items-center gap-2 flex-wrap">
              <p className="text-sm font-semibold truncate">{studio.name}</p>
              {isActive && (
                <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-primary/15 text-primary">
                  <CheckCircle2 className="h-3 w-3" aria-hidden />
                  Active
                </span>
              )}
              {!studio.isStudioActive && (
                <span className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-destructive/10 text-destructive">
                  Suspended
                </span>
              )}
            </div>
            <p className="text-xs text-muted-foreground">{studio.city}</p>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <Link
              to={`/s/${studio.slug}`}
              aria-label={`View ${studio.name} portfolio`}
              className="text-muted-foreground hover:text-foreground transition-colors"
              title="View portfolio"
            >
              <ExternalLink className="h-4 w-4" />
            </Link>

            {isActive ? (
              <Button size="sm" variant="outline" disabled className="gap-1.5 text-xs">
                <CheckCircle2 className="h-3.5 w-3.5" />
                Current
              </Button>
            ) : (
              <Button
                size="sm"
                variant="outline"
                onClick={() => onSwitch(studio.studioId)}
                disabled={isSwitching}
                className="text-xs gap-1.5"
                aria-label={`Switch to ${studio.name}`}
              >
                {isSwitching ? (
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                ) : null}
                Switch
              </Button>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function MyStudiosPage() {
  useDocumentMeta({ title: "My Studios — Pena e Artë", canonical: "/my-studios" });

  const dispatch        = useAppDispatch();
  const currentTenantId = useAppSelector((s) => s.auth.tenantId);
  const navigate        = useNavigate();

  const { data: studios, isLoading, isError, refetch } = useGetMyStudiosQuery();
  const [switchStudio]    = useSwitchStudioMutation();
  const [switchingId, setSwitchingId] = useState<string | null>(null);

  async function handleSwitch(studioId: string) {
    setSwitchingId(studioId);
    try {
      const response = await switchStudio({ studioId }).unwrap();
      const decoded  = decodeToken(response.accessToken);
      dispatch(setCredentials({ ...decoded, refreshToken: response.refreshToken }));
      toast.success(
        response.isNewMembership
          ? "Joined studio — welcome!"
          : "Studio switched successfully."
      );
      navigate("/book", { replace: true });
    } catch {
      toast.error("Couldn't switch studios. Please try again.");
    } finally {
      setSwitchingId(null);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">My Studios</span>
        {studios && studios.length > 0 && (
          <span className="text-xs text-muted-foreground ml-1">
            ({studios.length})
          </span>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {/* ── Loading ── */}
        {isLoading && (
          <div className="space-y-3" aria-label="Loading studios">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-20 w-full rounded-lg" />
            ))}
          </div>
        )}

        {/* ── Error ── */}
        {isError && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            Failed to load your studios.{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {/* ── Empty ── */}
        {!isLoading && !isError && studios?.length === 0 && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <Building2 className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium">No studios yet</p>
              <p className="text-xs text-muted-foreground">
                Visit a studio&apos;s page and tap &quot;Book&quot; to join.
              </p>
            </div>
            <Button size="sm" variant="outline" onClick={() => navigate("/discover")}>
              Discover studios
            </Button>
          </div>
        )}

        {/* ── List ── */}
        {!isLoading && !isError && studios && studios.length > 0 && (
          <>
            <p className="text-xs text-muted-foreground px-1">
              {studios.length === 1
                ? "You belong to one studio."
                : `You belong to ${studios.length} studios. Tap "Switch" to change your active studio.`}
            </p>

            {studios.map((studio) => (
              <StudioCard
                key={studio.studioId}
                studio={studio}
                isActive={studio.studioId === currentTenantId}
                isSwitching={switchingId === studio.studioId}
                onSwitch={handleSwitch}
              />
            ))}
          </>
        )}
      </main>
    </div>
  );
}
