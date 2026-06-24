import { useNavigate } from "react-router-dom";
import { Plus, ShieldCheck } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetDepositRulesQuery } from "../depositRulesApi";
import { DepositRuleCard } from "./DepositRuleCard";

export function DepositRuleListPage() {
  const navigate = useNavigate();
  const canManage = usePermission(Role.Owner);
  const { data: rules, isLoading, isError } = useGetDepositRulesQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Deposit Rules</span>
        </div>
        <div className="flex items-center gap-3">
          {rules && (
            <span className="text-xs text-muted-foreground">
              {rules.length} rule{rules.length !== 1 ? "s" : ""}
            </span>
          )}
          {canManage && (
            <Button size="sm" onClick={() => navigate("/deposit-rules/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              New Rule
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-2">
        {isLoading && (
          <div className="space-y-3" aria-label="Loading deposit rules">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-14 w-full rounded-lg" />
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load deposit rules. Please try again.
          </p>
        )}

        {!isLoading && !isError && rules?.length === 0 && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <ShieldCheck className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium text-foreground">No deposit rules yet</p>
              <p className="text-xs text-muted-foreground">
                Create a rule to automatically calculate deposits for new appointments.
              </p>
            </div>
            {canManage && (
              <Button size="sm" onClick={() => navigate("/deposit-rules/new")}>
                Create rule
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && rules && rules.length > 0 && rules.map((rule) => (
          <DepositRuleCard key={rule.id} rule={rule} />
        ))}
      </main>
    </div>
  );
}
