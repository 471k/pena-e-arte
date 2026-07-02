import { useState } from "react";
import { toast } from "sonner";
import { Loader2, Plus, Trash2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { useUpdateSessionSplitsMutation } from "../paymentsApi";
import type { SessionSplitItem, SessionSplitResponse } from "../payment.types";

interface SessionSplitsEditorProps {
  paymentId:     string;
  paymentAmount: number;
  currentSplits: SessionSplitResponse[];
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

export function SessionSplitsEditor({ paymentId, paymentAmount, currentSplits }: SessionSplitsEditorProps) {
  const [editing, setEditing] = useState(false);
  const [splits, setSplits]   = useState<SessionSplitItem[]>([]);
  const [updateSplits, { isLoading }] = useUpdateSessionSplitsMutation();

  function startEdit() {
    setSplits(
      currentSplits.length > 0
        ? currentSplits.map(({ label, amount }) => ({ label, amount }))
        : [{ label: "", amount: 0 }],
    );
    setEditing(true);
  }

  function addRow() {
    setSplits((prev) => [...prev, { label: "", amount: 0 }]);
  }

  function removeRow(index: number) {
    setSplits((prev) => prev.filter((_, i) => i !== index));
  }

  function setLabel(index: number, value: string) {
    setSplits((prev) => prev.map((s, i) => (i === index ? { ...s, label: value } : s)));
  }

  function setAmount(index: number, value: string) {
    setSplits((prev) =>
      prev.map((s, i) => (i === index ? { ...s, amount: parseFloat(value) || 0 } : s)),
    );
  }

  async function handleSave() {
    const valid = splits.filter((s) => s.label.trim() && s.amount > 0);
    if (valid.length === 0) return;
    try {
      await updateSplits({ id: paymentId, body: { splits: valid } }).unwrap();
      setEditing(false);
    } catch {
      toast.error("Failed to save splits. Please try again.");
    }
  }

  const runningTotal = splits.reduce((sum, s) => sum + (s.amount || 0), 0);
  const totalMatches = Math.round(runningTotal * 100) === Math.round(paymentAmount * 100);
  const canSave = totalMatches && splits.every((s) => s.label.trim() && s.amount > 0);

  if (!editing) {
    return (
      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <span className="text-sm font-medium">Session Splits</span>
          <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
            <Plus className="h-3.5 w-3.5" />
            {currentSplits.length > 0 ? "Edit" : "Add"} Splits
          </Button>
        </div>

        {currentSplits.length === 0 ? (
          <p className="text-xs text-muted-foreground">No session splits defined.</p>
        ) : (
          <div className="space-y-1.5">
            {currentSplits.map((split) => (
              <Card key={split.id}>
                <CardContent className="p-3 flex items-center justify-between gap-3">
                  <span className="text-sm">{split.label}</span>
                  <div className="text-right shrink-0">
                    <span className="text-sm font-medium">{formatCurrency(split.amount)}</span>
                    {split.paidAt && (
                      <p className="text-xs text-green-600">Paid</p>
                    )}
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">Session Splits</span>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => setEditing(false)}
          disabled={isLoading}
        >
          Cancel
        </Button>
      </div>

      <div className="space-y-2">
        {splits.map((split, index) => (
          <div key={index} className="flex gap-2 items-end">
            <div className="flex-1 space-y-1">
              <Label htmlFor={`split-label-${index}`} className="text-xs">
                Label
              </Label>
              <Input
                id={`split-label-${index}`}
                value={split.label}
                onChange={(e) => setLabel(index, e.target.value)}
                placeholder="e.g. Session 1"
              />
            </div>
            <div className="w-28 space-y-1">
              <Label htmlFor={`split-amount-${index}`} className="text-xs">
                Amount (€)
              </Label>
              <Input
                id={`split-amount-${index}`}
                type="number"
                step="0.01"
                min="0.01"
                value={split.amount || ""}
                onChange={(e) => setAmount(index, e.target.value)}
              />
            </div>
            <Button
              variant="ghost"
              size="icon"
              onClick={() => removeRow(index)}
              disabled={isLoading}
              className="h-9 w-9 text-muted-foreground hover:text-destructive shrink-0"
              aria-label="Remove split"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </div>
        ))}
      </div>

      <Button
        variant="outline"
        size="sm"
        onClick={addRow}
        disabled={isLoading}
        className="gap-1.5 w-full"
      >
        <Plus className="h-3.5 w-3.5" />
        Add Split
      </Button>

      <div className="flex items-center justify-between text-sm">
        <span className="text-muted-foreground">Total</span>
        <span className={totalMatches ? "font-medium" : "font-medium text-destructive"}>
          {formatCurrency(runningTotal)} / {formatCurrency(paymentAmount)}
        </span>
      </div>
      {!totalMatches && (
        <p role="alert" className="text-xs text-destructive">
          Splits must add up to {formatCurrency(paymentAmount)}.
        </p>
      )}

      <Button
        onClick={handleSave}
        disabled={isLoading || !canSave}
        className="w-full"
      >
        {isLoading ? (
          <>
            <Loader2 className="h-4 w-4 animate-spin" />
            Saving…
          </>
        ) : (
          "Save Splits"
        )}
      </Button>
    </div>
  );
}
