import { Link } from "react-router-dom";
import {
  BarChart3,
  Building2,
  CreditCard,
  EuroIcon,
  Loader2,
  Receipt,
  Share2,
  TrendingDown,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import {
  useGetPlatformStatsQuery,
  useGetPlatformSubscriptionsQuery,
} from "@/features/platform/platformApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";

interface KpiCardProps {
  label:   string;
  value:   string | number;
  icon:    React.ReactNode;
  href?:   string;
}

function KpiCard({ label, value, icon, href }: KpiCardProps) {
  const inner = (
    <Card className={href ? "hover:bg-muted/50 transition-colors" : ""}>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div>
          <p className="text-xs text-muted-foreground">{label}</p>
          <p className="text-2xl font-semibold tracking-tight">{value}</p>
        </div>
        <div className="text-muted-foreground">{icon}</div>
      </CardContent>
    </Card>
  );

  return href ? <Link to={href}>{inner}</Link> : inner;
}

interface AtRiskRowProps {
  sub: PlatformSubscriptionResponse;
}

function AtRiskRow({ sub }: AtRiskRowProps) {
  const daysLeft = Math.ceil(
    (new Date(sub.trialExpiresAt).getTime() - Date.now()) / 86_400_000
  );

  return (
    <div className="flex items-center justify-between py-2 border-b last:border-0">
      <div>
        <span className="text-sm font-medium">{sub.studioName}</span>
        <p className="text-xs text-muted-foreground">{sub.studioSlug}</p>
      </div>
      <span
        className={`text-xs px-2 py-0.5 rounded-full font-medium ${
          daysLeft <= 0
            ? "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300"
            : "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300"
        }`}
      >
        {daysLeft <= 0 ? "Expired" : `${daysLeft}d left`}
      </span>
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

export function IssuerDashboardPage() {
  const { data: stats, isLoading: statsLoading } = useGetPlatformStatsQuery();
  const { data: subscriptions }                  = useGetPlatformSubscriptionsQuery();

  const atRisk = subscriptions?.filter(
    (s) => s.status === "Trialing" &&
           new Date(s.trialExpiresAt).getTime() - Date.now() < 3 * 86_400_000
  ) ?? [];

  function formatCurrency(amount: number): string {
    return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
  }

  return (
    <div className="min-h-screen bg-background">
      <main className="max-w-3xl mx-auto px-4 py-8 space-y-8">
        <h1 className="text-xl font-semibold tracking-tight">Platform Overview</h1>

        {/* KPI grid */}
        {statsLoading ? (
          <div className="flex items-center justify-center py-8 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading stats…</span>
          </div>
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <KpiCard
              label="Total studios"
              value={stats?.totalStudios ?? 0}
              icon={<Building2 className="h-6 w-6" />}
              href="/platform/studios"
            />
            <KpiCard
              label="Active subs"
              value={stats?.activeSubscriptions ?? 0}
              icon={<Receipt className="h-6 w-6" />}
              href="/platform/subscriptions"
            />
            <KpiCard
              label="On trial"
              value={stats?.trialStudios ?? 0}
              icon={<CreditCard className="h-6 w-6" />}
              href="/platform/subscriptions"
            />
            <KpiCard
              label="MRR"
              value={formatCurrency(stats?.monthlyRecurringRevenue ?? 0)}
              icon={<EuroIcon className="h-6 w-6" />}
            />
          </div>
        )}

        {/* At-risk trials */}
        {atRisk.length > 0 && (
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-sm flex items-center gap-2">
                <TrendingDown className="h-4 w-4 text-yellow-500" />
                Trials expiring soon
              </CardTitle>
            </CardHeader>
            <CardContent className="pt-0">
              {atRisk.map((sub) => (
                <AtRiskRow key={sub.studioId} sub={sub} />
              ))}
            </CardContent>
          </Card>
        )}

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
