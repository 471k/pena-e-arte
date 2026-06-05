import { useNavigate } from "react-router-dom";
import { Loader2, Plus, ShieldCheck } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
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
          <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading rules…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load deposit rules. Please try again.
          </p>
        )}

        {!isLoading && !isError && rules?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No deposit rules configured yet.
          </p>
        )}

        {!isLoading && !isError && rules && rules.length > 0 && rules.map((rule) => (
          <DepositRuleCard key={rule.id} rule={rule} />
        ))}
      </main>
    </div>
  );
}
