import { useState } from "react";
import {
  Building2, CheckCircle2, ExternalLink, Bell, LogOut,
  Loader2, MoreVertical, Plus,
} from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/shared/components/ui/dropdown-menu";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import { StudioNotificationSheet } from "@/features/auth/components/StudioNotificationSheet";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { setCredentials, logout } from "@/features/auth/authSlice";
import { decodeToken } from "@/shared/utils/jwt";
import {
  useGetMyStudiosQuery,
  useSwitchStudioMutation,
  useLeaveStudioMutation,
} from "@/features/auth/authApi";
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
    <div
      className="h-12 w-12 rounded-md bg-muted text-muted-foreground/80
                 flex items-center justify-center text-sm font-semibold
                 shrink-0 border border-border/50"
    >
      {initials}
    </div>
  );
}

// ── Studio card ───────────────────────────────────────────────────────────────

interface StudioCardProps {
  studio:          MyStudioResponse;
  isActive:        boolean;
  isSwitching:     boolean;
  onSwitch:        (studioId: string) => void;
  onLeave:         (studio: MyStudioResponse) => void;
  onNotifications: (studio: MyStudioResponse) => void;
}

function StudioCard({ studio, isActive, isSwitching, onSwitch, onLeave, onNotifications }: StudioCardProps) {
  return (
    <Card
      className={`transition-colors ${
        isActive
          ? "border-emerald-500/40 bg-emerald-950/10"
          : "border-border/50"
      }`}
    >
      <CardContent className="p-4">
        <div className="flex items-start gap-3">
          <StudioAvatar name={studio.name} coverImageUrl={studio.coverImageUrl} />

          <div className="flex-1 min-w-0 space-y-0.5">
            <div className="flex items-center gap-2 flex-wrap">
              <p className="text-sm font-semibold truncate">{studio.name}</p>
              {isActive && (
                <span
                  className="inline-flex items-center gap-1 rounded-full px-2 py-0.5
                             text-xs font-medium bg-emerald-500/15 text-emerald-500"
                >
                  <CheckCircle2 className="h-3 w-3" aria-hidden />
                  Active
                </span>
              )}
              {!studio.isStudioActive && (
                <span
                  className="inline-flex items-center rounded-full px-2 py-0.5
                             text-xs font-medium bg-destructive/10 text-destructive"
                >
                  Suspended
                </span>
              )}
            </div>
            <p className="text-xs text-muted-foreground">{studio.city}</p>
          </div>

          <div className="flex items-center gap-1 shrink-0">
            {isActive ? (
              <span
                className="inline-flex items-center gap-1 rounded-full px-2.5 py-1
                           text-xs font-medium bg-emerald-500/15 text-emerald-500 shrink-0"
                aria-label={`${studio.name} is your current studio`}
              >
                <CheckCircle2 className="h-3 w-3" aria-hidden />
                Current
              </span>
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

            <DropdownMenu modal={false}>
              <DropdownMenuTrigger asChild>
                <Button
                  size="icon"
                  variant="ghost"
                  className="h-8 w-8"
                  aria-label={`More options for ${studio.name}`}
                >
                  <MoreVertical className="h-4 w-4" aria-hidden />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-48">
                <DropdownMenuItem asChild>
                  <Link
                    to={`/s/${studio.slug}`}
                    className="flex items-center gap-2 cursor-pointer"
                  >
                    <ExternalLink className="h-4 w-4" aria-hidden />
                    View public profile
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem
                  onSelect={() => {
                    // Deferred (not prevented — a prevented onSelect keeps the
                    // dropdown open indefinitely): opening a Dialog-based overlay
                    // synchronously from a DropdownMenuItem select races the menu's
                    // own close/focus-return behavior against the dialog's focus
                    // trap and can loop forever.
                    setTimeout(() => onNotifications(studio), 0);
                  }}
                  className="flex items-center gap-2"
                >
                  <Bell className="h-4 w-4" aria-hidden />
                  Manage notifications
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  onClick={() => onLeave(studio)}
                  className="flex items-center gap-2 text-destructive focus:text-destructive"
                >
                  <LogOut className="h-4 w-4" aria-hidden />
                  Leave studio
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
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
  const [leaveStudio]     = useLeaveStudioMutation();
  const [switchingId, setSwitchingId] = useState<string | null>(null);
  const [leaveTarget, setLeaveTarget] = useState<MyStudioResponse | null>(null);
  const [isLeaving, setIsLeaving]     = useState(false);
  const [notifTarget, setNotifTarget] = useState<MyStudioResponse | null>(null);

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

  async function handleLeave() {
    if (!leaveTarget) return;
    setIsLeaving(true);
    try {
      const result = await leaveStudio({ studioId: leaveTarget.studioId }).unwrap();
      toast.success(`Left ${leaveTarget.name}.`);
      if (result.isLeavingActiveTenant) {
        dispatch(logout());
        navigate("/discover", { replace: true });
      }
    } catch {
      toast.error("Couldn't leave the studio. Please try again.");
    } finally {
      setIsLeaving(false);
      setLeaveTarget(null);
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
        <Button
          size="sm"
          variant="ghost"
          className="ml-auto h-7 px-2 text-xs gap-1 text-muted-foreground hover:text-foreground"
          onClick={() => navigate("/discover")}
          aria-label="Discover more studios"
        >
          <Plus className="h-3.5 w-3.5" aria-hidden />
          Discover
        </Button>
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
            <div className="flex items-center justify-between px-1 gap-2">
              <p className="text-xs text-muted-foreground">
                {studios.length === 1
                  ? "You belong to 1 studio."
                  : `You belong to ${studios.length} studios. Tap "Switch" to change your active studio.`}
              </p>
              <Button
                size="sm"
                variant="ghost"
                className="shrink-0 h-7 px-2 text-xs gap-1 text-muted-foreground hover:text-foreground"
                onClick={() => navigate("/discover")}
                aria-label="Discover more studios to join"
              >
                <Plus className="h-3 w-3" aria-hidden />
                Join another
              </Button>
            </div>

            {studios.map((studio) => (
              <StudioCard
                key={studio.studioId}
                studio={studio}
                isActive={studio.studioId === currentTenantId}
                isSwitching={switchingId === studio.studioId}
                onSwitch={handleSwitch}
                onLeave={setLeaveTarget}
                onNotifications={setNotifTarget}
              />
            ))}
          </>
        )}
      </main>

      {/* ── Leave confirmation dialog ── */}
      <AlertDialog
        open={leaveTarget !== null}
        onOpenChange={(open) => !open && setLeaveTarget(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Leave {leaveTarget?.name}?</AlertDialogTitle>
            <AlertDialogDescription>
              You will lose access to this studio&apos;s booking flow.
              Your appointment history and records are preserved — you can
              rejoin the studio at any time.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isLeaving}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleLeave}
              disabled={isLeaving}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {isLeaving ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
              ) : (
                "Leave studio"
              )}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* ── Notification preferences sheet ── */}
      {notifTarget && (
        <StudioNotificationSheet
          studioId={notifTarget.studioId}
          studioName={notifTarget.name}
          open={notifTarget !== null}
          onClose={() => setNotifTarget(null)}
        />
      )}
    </div>
  );
}
