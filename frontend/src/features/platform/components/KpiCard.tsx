import { Link } from "react-router-dom";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";

export type KpiAccent = "default" | "info" | "warning" | "success" | "danger";

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

export function KpiCard({ label, value, icon, subtitle, href, accent = "default" }: KpiCardProps) {
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

export function KpiSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <Skeleton className="h-3 w-20" />
        <Skeleton className="h-8 w-16" />
      </CardContent>
    </Card>
  );
}
