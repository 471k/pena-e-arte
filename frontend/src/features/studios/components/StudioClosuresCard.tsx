import { useState } from "react";
import { Loader2, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import {
  useGetMyStudioQuery,
  useGetStudioClosuresQuery,
  useAddStudioClosureMutation,
  useDeleteStudioClosureMutation,
} from "../studiosApi";

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

export function StudioClosuresCard() {
  const { data: studio } = useGetMyStudioQuery();
  const { data: closures, isLoading } = useGetStudioClosuresQuery(studio?.id ?? "", { skip: !studio?.id });
  const [addClosure, { isLoading: adding }] = useAddStudioClosureMutation();
  const [deleteClosure, { isLoading: deleting }] = useDeleteStudioClosureMutation();

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate]     = useState("");
  const [reason, setReason]       = useState("");
  const [error, setError]         = useState<string | null>(null);

  if (!studio) return null;

  function resetForm() {
    setStartDate("");
    setEndDate("");
    setReason("");
    setError(null);
    setIsFormOpen(false);
  }

  async function handleAdd() {
    if (!startDate || !endDate || !reason.trim()) {
      setError("All fields are required.");
      return;
    }
    if (endDate < startDate) {
      setError("End date must be on or after start date.");
      return;
    }
    try {
      await addClosure({ id: studio!.id, body: { startDate, endDate, reason: reason.trim() } }).unwrap();
      toast.success("Closure added.");
      resetForm();
    } catch (err: unknown) {
      const message =
        err && typeof err === "object" && "data" in err && err.data &&
        typeof err.data === "object" && "message" in err.data
          ? String((err.data as { message: string }).message)
          : "Failed to add closure.";
      setError(message);
    }
  }

  async function handleDelete(closureId: string) {
    try {
      await deleteClosure({ id: studio!.id, closureId }).unwrap();
      toast.success("Closure removed.");
    } catch {
      toast.error("Failed to remove closure.");
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Studio closures</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          Block out dates when the whole studio is closed — public holidays, renovations,
          studio-wide vacation. No artist will appear bookable on these dates.
        </p>

        {isLoading ? (
          <div className="flex items-center gap-2 text-muted-foreground text-sm">
            <Loader2 className="h-4 w-4 animate-spin" />
            Loading…
          </div>
        ) : closures && closures.length > 0 ? (
          <ul className="space-y-2">
            {closures.map((c) => (
              <li
                key={c.id}
                className="flex items-center justify-between gap-3 rounded-md border px-3 py-2"
              >
                <div className="min-w-0">
                  <p className="text-sm font-medium truncate">{c.reason}</p>
                  <p className="text-xs text-muted-foreground">
                    {formatDate(c.startDate)} – {formatDate(c.endDate)}
                  </p>
                </div>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-7 w-7 shrink-0"
                  onClick={() => handleDelete(c.id)}
                  disabled={deleting}
                  aria-label={`Remove closure: ${c.reason}`}
                >
                  <Trash2 className="h-3.5 w-3.5 text-destructive" />
                </Button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground italic">No upcoming closures.</p>
        )}

        {isFormOpen ? (
          <div className="space-y-3 rounded-md border p-3">
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="closure-start">Start date</Label>
                <Input
                  id="closure-start"
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="closure-end">End date</Label>
                <Input
                  id="closure-end"
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="closure-reason">Reason</Label>
              <Input
                id="closure-reason"
                placeholder="e.g. Christmas holiday"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                maxLength={500}
              />
            </div>
            {error && <p className="text-xs text-destructive">{error}</p>}
            <div className="flex items-center gap-2">
              <Button size="sm" onClick={handleAdd} disabled={adding} className="gap-2">
                {adding && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Add closure
              </Button>
              <Button variant="ghost" size="sm" onClick={resetForm} disabled={adding} className="gap-1">
                <X className="h-3.5 w-3.5" />
                Cancel
              </Button>
            </div>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setIsFormOpen(true)}>
            Add closure
          </Button>
        )}
      </CardContent>
    </Card>
  );
}
