import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import {
  AlertTriangle, Banknote, Bell, BookOpen, CalendarDays, CreditCard,
  LayoutDashboard, Loader2, ScrollText, Scroll, Users, Zap,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import { cn } from "@/shared/utils/cn";
import { useGetSubscriptionQuery } from "@/features/billing/billingApi";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { AppointmentStatusBadge } from "@/features/appointments/components/AppointmentStatusBadge";
import { useGetPaymentsQuery } from "@/features/payments/paymentsApi";
import { CashDepositConfirmButton } from "@/features/payments/components/CashDepositConfirmButton";
import { PaymentStatus } from "@/features/payments/payment.types";
import type { SubscriptionResponse } from "@/features/billing/billing.types";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import type { ArtistResponse } from "@/features/artists/artistsApi";

// ── helpers ────────────────────────────────────────────────────────────────

function startOfDay(d: Date): Date {
  const out = new Date(d);
  out.setHours(0, 0, 0, 0);
  return out;
}

function addDays(d: Date, n: number): Date {
  const out = new Date(d);
  out.setDate(out.getDate() + n);
  return out;
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function formatDate(d: Date): string {
  return d.toLocaleDateString("en-GB", { weekday: "long", day: "numeric", month: "long" });
}

function daysUntil(iso: string): number {
  return Math.max(0, Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000));
}

// ── subscription banner ───────────────────────────────────────────────────

interface BannerConfig {
  bg:    string;
  icon:  React.ReactNode;
  text:  string;
  cta:   string;
  href:  string;
}

// Must match SubscriptionStatus JSON output from backend
export function bannerConfig(sub: SubscriptionResponse): BannerConfig | null {
  switch (sub.status) {
    case "Trialing":
      return {
        bg:   "border-blue-500/30 bg-blue-500/10 text-blue-700 dark:text-blue-300",
        icon: <Zap className="h-4 w-4 shrink-0" />,
        text: `Trial ends in ${daysUntil(sub.trialExpiresAt)} day${daysUntil(sub.trialExpiresAt) !== 1 ? "s" : ""}.`,
        cta:  "Subscribe",
        href: "/billing/subscribe",
      };
    case "GracePeriod":
      return {
        bg:   "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400",
        icon: <AlertTriangle className="h-4 w-4 shrink-0" />,
        text: `Trial expired — read-only mode. ${daysUntil(sub.gracePeriodEnd)} day${daysUntil(sub.gracePeriodEnd) !== 1 ? "s" : ""} left.`,
        cta:  "Subscribe now",
        href: "/billing/subscribe",
      };
    case "PastDue":
      return {
        bg:   "border-red-500/30 bg-red-500/10 text-red-700 dark:text-red-400",
        icon: <AlertTriangle className="h-4 w-4 shrink-0" />,
        text: "Last payment failed. Studio access may be restricted.",
        cta:  "Update billing",
        href: "/billing",
      };
    case "Cancelled":
      return {
        bg:   "border-red-500/30 bg-red-500/10 text-red-700 dark:text-red-400",
        icon: <AlertTriangle className="h-4 w-4 shrink-0" />,
        text: "Subscription cancelled.",
        cta:  "Reactivate",
        href: "/billing/subscribe",
      };
    default:
      return null;
  }
}

function SubscriptionBanner({ sub }: { sub: SubscriptionResponse }) {
  const navigate = useNavigate();
  const cfg = bannerConfig(sub);
  if (!cfg) return null;

  return (
    <div className={cn("flex items-center gap-3 rounded-lg border px-4 py-3 text-sm", cfg.bg)}>
      {cfg.icon}
      <span className="flex-1">{cfg.text}</span>
      <Button
        size="sm"
        variant="outline"
        className="h-7 shrink-0 border-current bg-transparent text-inherit hover:bg-current/10"
        onClick={() => navigate(cfg.href)}
      >
        {cfg.cta}
      </Button>
    </div>
  );
}

// ── today's schedule ──────────────────────────────────────────────────────

function artistName(artistId: string, artists: ArtistResponse[]): string {
  const a = artists.find((x) => x.id === artistId);
  return a ? `${a.firstName} ${a.lastName}` : "—";
}

function TodayRow({ appt, artists }: { appt: AppointmentResponse; artists: ArtistResponse[] }) {
  return (
    <div className="flex items-center justify-between py-2.5 gap-4">
      <div className="flex items-center gap-3 min-w-0">
        <span className="text-sm font-medium tabular-nums shrink-0">
          {formatTime(appt.date)}
        </span>
        <AppointmentStatusBadge status={appt.status} />
      </div>
      <span className="text-xs text-muted-foreground truncate">
        {artistName(appt.artistId, artists)}
      </span>
    </div>
  );
}

function TodaySection({
  appointments,
  artists,
  isLoading,
  isError,
}: {
  appointments: AppointmentResponse[] | undefined;
  artists:      ArtistResponse[];
  isLoading:    boolean;
  isError:      boolean;
}) {
  const navigate = useNavigate();

  return (
    <Card>
      <CardContent className="p-0">
        <div className="flex items-center justify-between px-4 pt-4 pb-2">
          <div className="flex items-center gap-2">
            <CalendarDays className="h-4 w-4 text-muted-foreground" />
            <span className="text-sm font-medium">Today</span>
            {appointments && appointments.length > 0 && (
              <span className="text-xs text-muted-foreground">
                {appointments.length} appointment{appointments.length !== 1 ? "s" : ""}
              </span>
            )}
          </div>
          <Button
            variant="ghost"
            size="sm"
            className="h-7 text-xs px-2"
            onClick={() => navigate("/schedule")}
          >
            Full schedule
          </Button>
        </div>

        <div className="px-4 pb-4">
          {isLoading && (
            <div className="flex items-center gap-2 py-4 text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span className="text-sm">Loading…</span>
            </div>
          )}

          {isError && (
            <p className="text-sm text-destructive py-4">Failed to load appointments.</p>
          )}

          {!isLoading && !isError && appointments?.length === 0 && (
            <p className="text-sm text-muted-foreground py-4 text-center">
              No appointments today.
            </p>
          )}

          {!isLoading && !isError && appointments && appointments.length > 0 && (
            <div>
              {appointments.map((a, i) => (
                <div key={a.id}>
                  {i > 0 && <Separator />}
                  <TodayRow appt={a} artists={artists} />
                </div>
              ))}
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

// ── cash-pending section ──────────────────────────────────────────────────

function CashPendingSection() {
  const { data: payments = [] } = useGetPaymentsQuery({ pageSize: 50 });

  const pending = payments.filter((p) => p.status === PaymentStatus.CashPending);
  if (pending.length === 0) return null;

  return (
    <Card>
      <CardContent className="p-0">
        <div className="flex items-center gap-2 px-4 pt-4 pb-2">
          <Banknote className="h-4 w-4 text-muted-foreground" />
          <span className="text-sm font-medium">Awaiting Cash</span>
          <span className="text-xs text-muted-foreground">{pending.length}</span>
        </div>
        <div className="px-4 pb-4 space-y-0">
          {pending.map((p, i) => (
            <div key={p.id}>
              {i > 0 && <Separator />}
              <div className="flex items-center justify-between py-2.5 gap-4">
                <div className="text-sm">
                  <span className="font-medium">{p.clientName}</span>
                  <span className="ml-2 text-xs text-muted-foreground">
                    €{p.amount.toFixed(2)}
                  </span>
                </div>
                <CashDepositConfirmButton
                  paymentId={p.id}
                  clientName={p.clientName}
                  amount={p.amount}
                />
              </div>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

// ── quick-nav tiles ───────────────────────────────────────────────────────

interface NavTile {
  label: string;
  icon:  React.ReactNode;
  href:  string;
}

const NAV_TILES: NavTile[] = [
  { label: "Schedule",      icon: <CalendarDays className="h-5 w-5" />, href: "/schedule" },
  { label: "Clients",       icon: <Users        className="h-5 w-5" />, href: "/clients" },
  { label: "Artists",       icon: <Scroll       className="h-5 w-5" />, href: "/artists" },
  { label: "Designs",       icon: <BookOpen     className="h-5 w-5" />, href: "/designs" },
  { label: "Deposit Rules", icon: <ScrollText   className="h-5 w-5" />, href: "/deposit-rules" },
  { label: "Billing",       icon: <CreditCard   className="h-5 w-5" />, href: "/billing" },
  { label: "Notifications", icon: <Bell         className="h-5 w-5" />, href: "/notifications" },
  { label: "Studio",        icon: <LayoutDashboard className="h-5 w-5" />, href: "/studios/me" },
];

function QuickNav() {
  const navigate = useNavigate();
  return (
    <div className="grid grid-cols-3 gap-3">
      {NAV_TILES.map(({ label, icon, href }) => (
        <button
          key={href}
          type="button"
          onClick={() => navigate(href)}
          className={cn(
            "flex flex-col items-center justify-center gap-2 rounded-lg border border-input",
            "bg-background px-3 py-4 text-sm text-muted-foreground",
            "hover:border-ring hover:text-foreground transition-colors",
          )}
        >
          {icon}
          <span className="text-xs font-medium leading-tight text-center">{label}</span>
        </button>
      ))}
    </div>
  );
}

// ── page ──────────────────────────────────────────────────────────────────

export function DashboardPage() {
  const today       = useMemo(() => new Date(), []);
  const todayStart  = useMemo(() => startOfDay(today), [today]);
  const tomorrow    = useMemo(() => addDays(todayStart, 1), [todayStart]);

  const { data: sub } = useGetSubscriptionQuery();
  const {
    data:      todayAppts,
    isLoading: loadingAppts,
    isError:   apptError,
  } = useGetAppointmentsQuery({
    from: todayStart.toISOString(),
    to:   tomorrow.toISOString(),
  });
  const { data: artists = [] } = useGetArtistsQuery(undefined);

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <LayoutDashboard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Dashboard</span>
        </div>
        <span className="text-xs text-muted-foreground">{formatDate(today)}</span>
      </header>

      <main className="max-w-lg mx-auto px-4 py-6 space-y-4">
        {sub && <SubscriptionBanner sub={sub} />}

        <TodaySection
          appointments={todayAppts}
          artists={artists}
          isLoading={loadingAppts}
          isError={apptError}
        />

        <CashPendingSection />

        <QuickNav />
      </main>
    </div>
  );
}
