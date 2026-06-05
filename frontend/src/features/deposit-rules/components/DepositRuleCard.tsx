import { Link } from "react-router-dom";
import { ChevronRight, DollarSign, Percent } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import type { DepositRuleResponse } from "../depositRule.types";

interface DepositRuleCardProps {
  rule: DepositRuleResponse;
}

function formatAmount(rule: DepositRuleResponse): string {
  if (rule.amountFixed !== null) {
    return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(rule.amountFixed);
  }
  return `${rule.amountPercent}%`;
}

export function DepositRuleCard({ rule }: DepositRuleCardProps) {
  const isFixed = rule.amountFixed !== null;

  return (
    <Link
      to={`/deposit-rules/${rule.id}`}
      className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
    >
      <Card className="hover:bg-muted/40 transition-colors">
        <CardContent className="p-4 flex items-center gap-4">
          <div className={cn(
            "flex h-10 w-10 shrink-0 items-center justify-center rounded-full",
            isFixed ? "bg-blue-500/10 text-blue-600" : "bg-purple-500/10 text-purple-600",
          )}>
            {isFixed ? <DollarSign className="h-4 w-4" /> : <Percent className="h-4 w-4" />}
          </div>

          <div className="min-w-0 flex-1 space-y-1">
            <p className="text-sm font-medium leading-none">{rule.name}</p>
            <p className="text-xs text-muted-foreground">
              {isFixed ? "Fixed" : "Percent"} · {formatAmount(rule)}
            </p>
          </div>

          <div className="flex items-center gap-2">
            <span className={cn(
              "text-xs px-2 py-0.5 rounded-full font-medium",
              rule.isActive
                ? "bg-green-500/10 text-green-700"
                : "bg-muted text-muted-foreground",
            )}>
              {rule.isActive ? "Active" : "Inactive"}
            </span>
            <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
          </div>
        </CardContent>
      </Card>
    </Link>
  );
}
