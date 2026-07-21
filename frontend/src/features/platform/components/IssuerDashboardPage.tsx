import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  AlertCircle,
  AlertTriangle,
  Ban,
  Building2,
  Clock,
  CreditCard,
  PlusCircle,
  TrendingUp,
  Users,
  XCircle,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetPlatformStatsQuery,
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
} from "@/features/platform/platformApi";
import { MrrChart } from "./MrrChart";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";

type KpiAccent = "default" | "info" | "warning" | "success" | "danger";

const ACCENT_ICON_COLOR: Record<KpiAccent, string> = {
  default: "text-muted-foreground",
  info:    "text-blue-500",
  warning: "text-amber-500",
  success: "text-emerald-500",
  danger:  "text-red-500",
};

interface KpiCardProps {
  label:    string;
  value:    string | number;
  icon:     React.ReactNode;
  subtitle?: string;
  href?:    string;
  accent?:  KpiAccent;
}

function KpiCard({ label, value, icon, subtitle, href, accent = "default" }: KpiCardProps) {
  const inner = (
    <Card className={href ? "hover:bg-muted/50 transition-colors" : ""}>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div>
          <p className="text-xs text-muted-foreground">{label}</p>
          <p className="text-2xl font-semibold tracking-tight">{value}</p>
          {subtitle && (
            <p className="text-[10px] text-muted-foreground mt-0.5">{subtitle}</p>
          )}
        </div>
        <div className={ACCENT_ICON_COLOR[accent]}>{icon}</div>
      </CardContent>
    </Card>
  );

  return href ? <Link to={href}>{inner}</Link> : inner;
}

function KpiSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <Skeleton className="h-3 w-20" />
        <Skeleton className="h-8 w-16" />
      </CardContent>
    </Card>
  );
}

function KpiGridSkeleton() {
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <KpiSkeleton /><KpiSkeleton /><KpiSkeleton /><KpiSkeleton />
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <KpiSkeleton /><KpiSkeleton /><KpiSkeleton /><KpiSkeleton />
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <KpiSkeleton /><KpiSkeleton /><KpiSkeleton />
      </div>
    </div>
  );
}

const AT_RISK_STATUSES = new Set(["GracePeriod", "PastDue"]);

interface AtRiskRowProps {
  sub:           PlatformSubscriptionResponse;
  hasDuplicate?: boolean;
}

function daysUntilExpiry(dateStr: string): number {
  const expiry = new Date(dateStr).getTime();
  const now    = Date.now();
  return Math.ceil((expiry - now) / 86_400_000);
}

function ExpiryLabel({ dateStr, status }: { dateStr: string; status: string }) {
  const days = daysUntilExpiry(dateStr);
  if (status === "PastDue") {
    return (
      <p className="text-xs text-red-600 dark:text-red-400 mt-0.5 font-medium">
        Payment overdue
      </p>
    );
  }
  if (days <= 0) {
    return (
      <p className="text-xs text-red-600 dark:text-red-400 mt-0.5 font-medium">
        Expires today
      </p>
    );
  }
  if (days <= 3) {
    return (
      <p className="text-xs text-amber-600 dark:text-amber-400 mt-0.5 font-medium">
        {days} day{days !== 1 ? "s" : ""} left
      </p>
    );
  }
  return (
    <p className="text-xs text-muted-foreground mt-0.5">
      {days} days left
    </p>
  );
}

function AtRiskRow({ sub, hasDuplicate }: AtRiskRowProps) {
  const [extending,  setExtending]  = useState(false);
  const [days,       setDays]       = useState("7");
  const [extendTrial, { isLoading }] = useExtendTrialMutation();

  async function handleExtend() {
    const additionalDays = parseInt(days, 10);
    if (isNaN(additionalDays) || additionalDays < 1 || additionalDays > 90) return;
    try {
      await extendTrial({ studioId: sub.studioId, additionalDays }).unwrap();
      toast.success(`Trial extended by ${additionalDays} day${additionalDays !== 1 ? "s" : ""}`);
      setExtending(false);
    } catch {
      toast.error("Failed to extend trial");
    }
  }

  return (
    <div className="py-2 border-b last:border-0 space-y-1.5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-1.5 flex-wrap">
            <span className="text-sm font-medium">{sub.studioName}</span>
            <span
              className={`text-xs font-mono ${
                hasDuplicate
                  ? "text-foreground font-semibold"
                  : "text-muted-foreground"
              }`}
            >
              {sub.studioSlug}
            </span>
          </div>
          <div className="flex items-center gap-2 mt-0.5">
            <span
              className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                sub.status === "PastDue"
                  ? "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300"
                  : "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300"
              }`}
            >
              {sub.status === "PastDue" ? "Past due" : "Grace period"}
            </span>
            <ExpiryLabel
              dateStr={sub.currentPeriodEnd}
              status={sub.status}
            />
          </div>
        </div>

        <div className="flex items-center gap-1.5 shrink-0">
          {!extending && (
            <button
              onClick={() => setExtending(true)}
              className="text-xs px-2 py-1 rounded border hover:bg-muted transition-colors"
            >
              Extend trial
            </button>
          )}
          <Link
            to="/platform/studios"
            state={{ highlight: sub.studioId }}
            className="text-xs text-muted-foreground hover:text-foreground transition-colors px-1"
            title="Open studio"
          >
            →
          </Link>
        </div>
      </div>

      {extending && (
        <div className="flex items-center gap-2">
          <input
            type="number"
            min="1"
            max="90"
            value={days}
            onChange={(e) => setDays(e.target.value)}
            className="h-7 w-16 rounded border border-input bg-background px-2 text-xs"
          />
          <span className="text-xs text-muted-foreground">days</span>
          <button
            onClick={handleExtend}
            disabled={isLoading}
            className="text-xs px-2 py-1 rounded bg-primary text-primary-foreground hover:opacity-90 transition-opacity disabled:opacity-50"
          >
            {isLoading ? "…" : "Confirm"}
          </button>
          <button
            onClick={() => setExtending(false)}
            className="text-xs px-2 py-1 rounded hover:bg-muted transition-colors text-muted-foreground"
          >
            Cancel
          </button>
        </div>
      )}
    </div>
  );
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-GB", {
    style:                 "currency",
    currency:              "EUR",
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(amount);
}

function formatPercent(rate: number): string {
  return `${(rate * 100).toFixed(1)}%`;
}

export function IssuerDashboardPage() {
  useDocumentMeta({ title: "Platform Overview — Platform Admin", canonical: "/platform" });

  const { data: stats, isLoading: statsLoading } =
    useGetPlatformStatsQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: subscriptions } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });

  const atRisk      = subscriptions?.filter((s) => AT_RISK_STATUSES.has(s.status)) ?? [];
  const atRiskNames = atRisk.map((s) => s.studioName);

  const newThisMonthCaveat =
    stats && stats.totalStudios > 0 && stats.newStudiosThisMonth === stats.totalStudios
      ? "incl. test data"
      : "this calendar month";

  return (
    <div className="min-h-screen bg-background">
      <main className="max-w-3xl mx-auto px-4 py-8 space-y-8">
        <h1 className="text-xl font-semibold tracking-tight">Platform Overview</h1>

        {/* KPI grid */}
        {statsLoading ? (
          <KpiGridSkeleton />
        ) : (
          <div className="space-y-3">
            {/* Row 1 — totals */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <KpiCard
                label="Total Studios"
                value={stats?.totalStudios ?? 0}
                icon={<Building2 className="h-6 w-6" />}
                subtitle="all tenants"
                href="/platform/studios"
              />
              <KpiCard
                label="Active Subscriptions"
                value={stats?.activeSubscriptions ?? 0}
                icon={<CreditCard className="h-6 w-6" />}
                subtitle="current"
                href="/platform/subscriptions?status=Active"
                accent="success"
              />
              <KpiCard
                label="MRR"
                value={formatCurrency(stats?.mrr ?? 0)}
                icon={<TrendingUp className="h-6 w-6" />}
                subtitle={
                  stats?.mrrGrowthPercent !== undefined
                    ? `${stats.mrrGrowthPercent >= 0 ? "+" : ""}${stats.mrrGrowthPercent.toFixed(1)}% vs last month`
                    : undefined
                }
                accent={stats?.mrrGrowthPercent != null && stats.mrrGrowthPercent > 0 ? "success" : "default"}
              />
              <KpiCard
                label="ARPU"
                value={
                  stats && stats.activeSubscriptions > 0
                    ? formatCurrency(stats.mrr / stats.activeSubscriptions)
                    : "—"
                }
                icon={<Users className="h-6 w-6" />}
                subtitle="MRR ÷ active"
                accent="info"
              />
            </div>

            {/* Row 2 — pipeline */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <KpiCard
                label="Trialing"
                value={stats?.trialStudios ?? 0}
                icon={<Clock className="h-6 w-6" />}
                subtitle="current"
                href="/platform/subscriptions?status=Trialing"
                accent="info"
              />
              <KpiCard
                label="Grace Period"
                value={stats?.gracePeriodStudios ?? 0}
                icon={<AlertCircle className="h-6 w-6" />}
                subtitle="current"
                href="/platform/subscriptions?status=GracePeriod"
                accent="warning"
              />
              <KpiCard
                label="Past Due"
                value={stats?.pastDueStudios ?? 0}
                icon={<AlertTriangle className="h-6 w-6" />}
                subtitle="current"
                href="/platform/subscriptions?status=PastDue"
                accent="danger"
              />
              <KpiCard
                label="Cancelled"
                value={stats?.cancelledStudios ?? 0}
                icon={<XCircle className="h-6 w-6" />}
                subtitle="current"
                href="/platform/subscriptions?status=Cancelled"
              />
            </div>

            {/* Row 3 — health */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <KpiCard
                label="Trial Conversion"
                value={formatPercent(stats?.trialConversionRate ?? 0)}
                icon={<TrendingUp className="h-6 w-6" />}
                subtitle="active ÷ (active + trial + grace)"
              />
              <KpiCard
                label="New This Month"
                value={stats?.newStudiosThisMonth ?? 0}
                icon={<PlusCircle className="h-6 w-6" />}
                subtitle={newThisMonthCaveat}
                href="/platform/studios"
                accent="success"
              />
              <KpiCard
                label="Suspended"
                value={stats?.suspendedStudios ?? 0}
                icon={<Ban className="h-6 w-6" />}
                subtitle="deactivated by issuer"
                href="/platform/studios"
                accent={stats?.suspendedStudios ? "danger" : "default"}
              />
            </div>
          </div>
        )}

        {/* MRR chart */}
        <MrrChart />

        {/* At-risk studios */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-amber-500" />
              At-Risk Studios
              {atRisk.length > 0 && (
                <span className="ml-1 text-xs bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300 px-1.5 py-0.5 rounded-full font-medium">
                  {atRisk.length}
                </span>
              )}
            </CardTitle>
          </CardHeader>
          <CardContent className="pt-0">
            {atRisk.length === 0 ? (
              <p className="text-sm text-muted-foreground py-2">No at-risk studios.</p>
            ) : (
              atRisk.map((sub) => (
                <AtRiskRow
                  key={sub.studioId}
                  sub={sub}
                  hasDuplicate={atRiskNames.filter((n) => n === sub.studioName).length > 1}
                />
              ))
            )}
          </CardContent>
        </Card>

        <Link
          to="/platform/help-insights"
          className="block text-xs text-muted-foreground hover:text-foreground transition-colors"
        >
          View Help search insights →
        </Link>
      </main>
    </div>
  );
}
