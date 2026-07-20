import { useMemo, type ReactNode } from "react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useNavigate } from "react-router-dom";
import {
  AlertTriangle, Banknote, CalendarDays, ChevronRight,
  LayoutDashboard, Zap,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import { cn } from "@/shared/utils/cn";
import { useGetSubscriptionQuery } from "@/features/billing/billingApi";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { DepositStatus } from "@/features/appointments/appointment.types";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { AppointmentStatusBadge } from "@/features/appointments/components/AppointmentStatusBadge";
import { useGetPaymentsQuery } from "@/features/payments/paymentsApi";
import { CashDepositConfirmButton } from "@/features/payments/components/CashDepositConfirmButton";
import { PaymentStatus } from "@/features/payments/payment.types";
import type { SubscriptionResponse } from "@/features/billing/billing.types";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import { SetupChecklist } from "./SetupChecklist";

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
  return new Date(iso).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
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
  icon:  ReactNode;
  text:  string;
  cta:   string;
  href:  string;
}

// Must match SubscriptionStatus JSON output from backend
// eslint-disable-next-line react-refresh/only-export-components
export function bannerConfig(sub: SubscriptionResponse): BannerConfig | null {
  switch (sub.status) {
    case "Trialing":
      return {
        bg:   "border-blue-500/30 bg-blue-500/10 text-blue-700 dark:text-blue-300",
        icon: <Zap className="h-4 w-4 shrink-0" />,
        text: `Trial ends in ${daysUntil(sub.trialExpiresAt ?? sub.currentPeriodEnd)} day${daysUntil(sub.trialExpiresAt ?? sub.currentPeriodEnd) !== 1 ? "s" : ""}.`,
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

function AppointmentRowSkeleton() {
  return (
    <div
      className="flex items-center gap-3 py-2"
      data-testid="appointment-skeleton"
      aria-hidden="true"
    >
      <Skeleton className="h-8 w-8 rounded-full" />
      <div className="flex-1 space-y-1">
        <Skeleton className="h-3 w-1/3" />
        <Skeleton className="h-3 w-1/2" />
      </div>
      <Skeleton className="h-5 w-16 rounded-full" />
    </div>
  );
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
  const errorMessage = useSuspensionAwareError(isError, "Failed to load appointments.");

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
            variant="link"
            size="sm"
            className="h-7 text-xs px-2 gap-1"
            onClick={() => navigate("/schedule")}
          >
            View schedule
            <ChevronRight className="h-3 w-3" />
          </Button>
        </div>

        <div className="px-4 pb-4">
          {isLoading && (
            <div className="space-y-2" aria-label="Loading appointments">
              <AppointmentRowSkeleton />
              <AppointmentRowSkeleton />
              <AppointmentRowSkeleton />
            </div>
          )}

          {errorMessage && (
            <p className="text-sm text-destructive py-4" role="alert">{errorMessage}</p>
          )}

          {!isLoading && !isError && appointments?.length === 0 && (
            <div className="py-6 flex flex-col items-center gap-3 text-center">
              <p className="text-sm text-muted-foreground">No appointments today.</p>
              <div className="flex gap-2">
                <Button size="sm" onClick={() => navigate("/schedule")}>
                  Book Appointment
                </Button>
                <Button variant="ghost" size="sm" onClick={() => navigate("/schedule")}>
                  View this week →
                </Button>
              </div>
            </div>
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

// ── stat card ─────────────────────────────────────────────────────────────

interface StatCardProps {
  label:     string;
  value:     number;
  icon:      ReactNode;
  isLoading: boolean;
  testId?:   string;
}

function StatCard({ label, value, icon, isLoading, testId }: StatCardProps) {
  return (
    <div
      className="rounded-xl border border-border bg-card p-4 flex flex-col gap-1"
      data-testid={testId}
    >
      <div className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        {icon}
        {label}
      </div>
      {isLoading ? (
        <Skeleton className="h-8 w-12 mt-1" />
      ) : (
        <span className="text-3xl font-bold tabular-nums">{value}</span>
      )}
    </div>
  );
}

// ── page ──────────────────────────────────────────────────────────────────

export function DashboardPage() {
  const navigate    = useNavigate();
  const today       = useMemo(() => new Date(), []);
  const todayStart  = useMemo(() => startOfDay(today), [today]);
  const tomorrow    = useMemo(() => addDays(todayStart, 1), [todayStart]);
  const weekEnd     = useMemo(() => addDays(todayStart, 7), [todayStart]);

  const { data: sub } = useGetSubscriptionQuery();
  const {
    data:      todayAppts,
    isLoading: loadingAppts,
    isError:   apptError,
  } = useGetAppointmentsQuery({
    from: todayStart.toISOString(),
    to:   tomorrow.toISOString(),
  });
  const {
    data:      weekAppts,
    isLoading: loadingWeekAppts,
  } = useGetAppointmentsQuery({
    from: todayStart.toISOString(),
    to:   weekEnd.toISOString(),
  });
  const { data: artists = [] } = useGetArtistsQuery(undefined);

  const pendingDeposits = useMemo(
    () => weekAppts?.filter((a) => a.depositStatus === DepositStatus.Pending).length ?? 0,
    [weekAppts],
  );

  return (
    <div className="min-h-screen bg-background">
      <div className="flex items-center justify-between px-6 py-3 border-b bg-background">
        <div className="flex items-center gap-2">
          <LayoutDashboard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Dashboard</span>
        </div>
        <div className="flex items-center gap-3">
          <Button size="sm" onClick={() => navigate("/schedule")}>
            + Book Appointment
          </Button>
          <span className="text-xs text-muted-foreground">{formatDate(today)}</span>
        </div>
      </div>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        {sub && <SubscriptionBanner sub={sub} />}
        <SetupChecklist />

        {/* KPI stat cards */}
        <div className="grid grid-cols-3 gap-3">
          <StatCard
            label="Today"
            value={todayAppts?.length ?? 0}
            icon={<CalendarDays className="h-3.5 w-3.5" />}
            isLoading={loadingAppts}
            testId="stat-today"
          />
          <StatCard
            label="This Week"
            value={weekAppts?.length ?? 0}
            icon={<CalendarDays className="h-3.5 w-3.5" />}
            isLoading={loadingWeekAppts}
            testId="stat-week"
          />
          <StatCard
            label="Deposits Due"
            value={pendingDeposits}
            icon={<Banknote className="h-3.5 w-3.5" />}
            isLoading={loadingWeekAppts}
            testId="stat-deposits"
          />
        </div>

        <TodaySection
          appointments={todayAppts}
          artists={artists}
          isLoading={loadingAppts}
          isError={apptError}
        />

        <CashPendingSection />
      </main>
    </div>
  );
}
