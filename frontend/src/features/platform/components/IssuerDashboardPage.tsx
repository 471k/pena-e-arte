import { Link } from "react-router-dom";
import {
  AlertCircle,
  AlertTriangle,
  BarChart3,
  Building2,
  Clock,
  CreditCard,
  PlusCircle,
  Receipt,
  Share2,
  TrendingUp,
  Users,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetPlatformStatsQuery,
  useGetPlatformSubscriptionsQuery,
} from "@/features/platform/platformApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";

type KpiAccent = "default" | "info" | "warning" | "success";

const ACCENT_ICON_COLOR: Record<KpiAccent, string> = {
  default: "text-muted-foreground",
  info:    "text-blue-500",
  warning: "text-amber-500",
  success: "text-emerald-500",
};

interface KpiCardProps {
  label:   string;
  value:   string | number;
  icon:    React.ReactNode;
  href?:   string;
  accent?: KpiAccent;
}

function KpiCard({ label, value, icon, href, accent = "default" }: KpiCardProps) {
  const inner = (
    <Card className={href ? "hover:bg-muted/50 transition-colors" : ""}>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div>
          <p className="text-xs text-muted-foreground">{label}</p>
          <p className="text-2xl font-semibold tracking-tight">{value}</p>
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
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <KpiSkeleton /><KpiSkeleton /><KpiSkeleton />
      </div>
    </div>
  );
}

const AT_RISK_STATUSES = new Set(["GracePeriod", "PastDue"]);

interface AtRiskRowProps {
  sub: PlatformSubscriptionResponse;
}

function AtRiskRow({ sub }: AtRiskRowProps) {
  const expiry = new Date(sub.trialExpiresAt).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });

  return (
    <div className="flex items-center justify-between py-2 border-b last:border-0">
      <div>
        <span className="text-sm font-medium">{sub.studioName}</span>
        <p className="text-xs text-muted-foreground">{sub.studioSlug}</p>
      </div>
      <div className="text-right">
        <span
          className={`text-xs px-2 py-0.5 rounded-full font-medium ${
            sub.status === "PastDue"
              ? "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300"
              : "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300"
          }`}
        >
          {sub.status === "PastDue" ? "Past due" : "Grace period"}
        </span>
        <p className="text-xs text-muted-foreground mt-0.5">expires {expiry}</p>
      </div>
    </div>
  );
}

const QUICK_NAV = [
  { label: "Studios",       href: "/platform/studios",       icon: <Building2  className="h-5 w-5" /> },
  { label: "Plans",         href: "/platform/plans",         icon: <CreditCard className="h-5 w-5" /> },
  { label: "Subscriptions", href: "/platform/subscriptions", icon: <Receipt    className="h-5 w-5" /> },
  { label: "Referrals",     href: "/platform/referrals",     icon: <Share2     className="h-5 w-5" /> },
  { label: "Reports",       href: "/platform/reports",       icon: <BarChart3  className="h-5 w-5" /> },
];

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function formatPercent(rate: number): string {
  return `${(rate * 100).toFixed(1)}%`;
}

export function IssuerDashboardPage() {
  const { data: stats, isLoading: statsLoading } =
    useGetPlatformStatsQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: subscriptions } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });

  const atRisk = subscriptions?.filter((s) => AT_RISK_STATUSES.has(s.status)) ?? [];

  return (
    <div className="min-h-screen bg-background">
      <main className="max-w-3xl mx-auto px-4 py-8 space-y-8">
        <h1 className="text-xl font-semibold tracking-tight">Platform Overview</h1>

        {/* KPI grid */}
        {statsLoading ? (
          <KpiGridSkeleton />
        ) : (
          <div className="space-y-3">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <KpiCard
                label="Total Studios"
                value={stats?.totalStudios ?? 0}
                icon={<Building2 className="h-6 w-6" />}
                href="/platform/studios"
              />
              <KpiCard
                label="Active Subscriptions"
                value={stats?.activeSubscriptions ?? 0}
                icon={<CreditCard className="h-6 w-6" />}
                href="/platform/subscriptions"
              />
              <KpiCard
                label="MRR"
                value={formatCurrency(stats?.mrr ?? 0)}
                icon={<TrendingUp className="h-6 w-6" />}
              />
              <KpiCard
                label="Trial Conversion"
                value={formatPercent(stats?.trialConversionRate ?? 0)}
                icon={<Users className="h-6 w-6" />}
              />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <KpiCard
                label="Trialing Studios"
                value={stats?.trialStudios ?? 0}
                icon={<Clock className="h-6 w-6" />}
                href="/platform/subscriptions"
                accent="info"
              />
              <KpiCard
                label="Grace Period"
                value={stats?.gracePeriodStudios ?? 0}
                icon={<AlertCircle className="h-6 w-6" />}
                href="/platform/subscriptions"
                accent="warning"
              />
              <KpiCard
                label="New This Month"
                value={stats?.newStudiosThisMonth ?? 0}
                icon={<PlusCircle className="h-6 w-6" />}
                href="/platform/studios"
                accent="success"
              />
            </div>
          </div>
        )}

        {/* At-risk studios */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-amber-500" />
              At-Risk Studios
            </CardTitle>
          </CardHeader>
          <CardContent className="pt-0">
            {atRisk.length === 0 ? (
              <p className="text-sm text-muted-foreground py-2">No at-risk studios.</p>
            ) : (
              atRisk.map((sub) => <AtRiskRow key={sub.studioId} sub={sub} />)
            )}
          </CardContent>
        </Card>

        {/* Quick nav */}
        <div>
          <h2 className="text-sm font-medium text-muted-foreground mb-3">Quick navigation</h2>
          <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
            {QUICK_NAV.map(({ label, href, icon }) => (
              <Link key={href} to={href}>
                <Card className="hover:bg-muted/50 transition-colors">
                  <CardContent className="p-4 flex flex-col items-center gap-2">
                    <div className="text-muted-foreground">{icon}</div>
                    <span className="text-xs font-medium">{label}</span>
                  </CardContent>
                </Card>
              </Link>
            ))}
          </div>
        </div>
      </main>
    </div>
  );
}
