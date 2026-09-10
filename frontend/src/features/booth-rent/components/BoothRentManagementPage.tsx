import { useState } from "react";
import { toast } from "sonner";
import { Banknote, Plus, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Badge } from "@/shared/components/ui/badge";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogTrigger,
} from "@/shared/components/ui/dialog";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { DataTable } from "@/shared/components/DataTable";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import {
  useGetBoothRentSchedulesQuery, useCreateBoothRentScheduleMutation,
  useGetBoothRentChargesQuery, useMarkBoothRentChargeSettledMutation,
} from "../boothRentApi";
import { RentFrequency, type BoothRentChargeResponse } from "../boothRent.types";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

function NewScheduleDialog() {
  const { data: artists } = useGetArtistsQuery(undefined);
  const [createSchedule, { isLoading }] = useCreateBoothRentScheduleMutation();
  const [open, setOpen] = useState(false);
  const [artistId, setArtistId] = useState("");
  const [amount, setAmount] = useState("");
  const [frequency, setFrequency] = useState<RentFrequency>(RentFrequency.Weekly);
  const [nextChargeDate, setNextChargeDate] = useState("");

  async function handleSubmit() {
    if (!artistId || !amount || !nextChargeDate) return;
    const result = await createSchedule({
      artistId,
      amountFixed: Number(amount),
      frequency,
      nextChargeDate: new Date(nextChargeDate).toISOString(),
      isActive: true,
    });
    if ("data" in result) {
      toast.success("Booth-rent schedule created.");
      setOpen(false);
      setArtistId(""); setAmount(""); setNextChargeDate("");
    } else {
      toast.error("Failed to create booth-rent schedule.");
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button size="sm" className="gap-1.5"><Plus className="h-3.5 w-3.5" />New schedule</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader><DialogTitle>New booth-rent schedule</DialogTitle></DialogHeader>
        <div className="space-y-3 py-2">
          <div className="space-y-1.5">
            <Label htmlFor="booth-rent-artist">Artist</Label>
            <Select value={artistId} onValueChange={setArtistId}>
              <SelectTrigger id="booth-rent-artist"><SelectValue placeholder="Select an artist" /></SelectTrigger>
              <SelectContent>
                {artists?.map((a) => (
                  <SelectItem key={a.id} value={a.id}>{a.firstName} {a.lastName}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="booth-rent-amount">Amount (€)</Label>
            <Input id="booth-rent-amount" type="number" min="0.01" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="booth-rent-frequency">Frequency</Label>
            <Select value={frequency} onValueChange={(v) => setFrequency(v as RentFrequency)}>
              <SelectTrigger id="booth-rent-frequency"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={RentFrequency.Weekly}>Weekly</SelectItem>
                <SelectItem value={RentFrequency.Monthly}>Monthly</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="booth-rent-next-date">First charge date</Label>
            <Input id="booth-rent-next-date" type="date" value={nextChargeDate} onChange={(e) => setNextChargeDate(e.target.value)} />
          </div>
        </div>
        <DialogFooter>
          <Button onClick={handleSubmit} disabled={isLoading || !artistId || !amount || !nextChargeDate} className="gap-1.5">
            {isLoading && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Create schedule
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function SettleButton({ charge }: { charge: BoothRentChargeResponse }) {
  const [markSettled, { isLoading }] = useMarkBoothRentChargeSettledMutation();

  async function handleClick() {
    const result = await markSettled({ id: charge.id, body: { settledNote: null } });
    if ("data" in result) toast.success("Marked as settled.");
    else toast.error("Failed to mark charge settled.");
  }

  if (charge.isSettled) return <Badge variant="outline" className="border-green-300 bg-green-100 text-green-800">Settled</Badge>;
  return (
    <Button size="sm" variant="outline" className="h-7 text-xs" disabled={isLoading} onClick={handleClick}>
      Mark settled
    </Button>
  );
}

/** Owner-only booth-rent schedule + charge management. */
export function BoothRentManagementPage() {
  useDocumentMeta({ title: "Booth Rent — TattooOS", canonical: "/booth-rent" });

  const { data: schedules, isLoading: schedulesLoading } = useGetBoothRentSchedulesQuery();
  const { data: charges, isLoading: chargesLoading } = useGetBoothRentChargesQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <Banknote className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Booth Rent</span>
        </div>
        <NewScheduleDialog />
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-8">
        <section className="space-y-3">
          <h2 className="text-sm font-semibold text-muted-foreground">Schedules</h2>
          {schedulesLoading && <Skeleton className="h-24 w-full rounded-lg" />}
          {!schedulesLoading && schedules?.length === 0 && (
            <p className="text-sm text-muted-foreground py-6 text-center">No booth-rent schedules yet.</p>
          )}
          {!schedulesLoading && schedules && schedules.length > 0 && (
            <div className="space-y-2">
              {schedules.map((s) => (
                <div key={s.id} className="flex items-center justify-between rounded-lg border p-3">
                  <div>
                    <p className="text-sm font-medium">{s.artistName}</p>
                    <p className="text-xs text-muted-foreground">
                      {formatCurrency(s.amountFixed)} / {s.frequency.toLowerCase()} — next charge {formatDate(s.nextChargeDate)}
                    </p>
                  </div>
                  <Badge variant="outline" className={s.isActive ? "border-green-300 bg-green-100 text-green-800" : "border-slate-300 bg-slate-100 text-slate-800"}>
                    {s.isActive ? "Active" : "Inactive"}
                  </Badge>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="space-y-3">
          <h2 className="text-sm font-semibold text-muted-foreground">Charges</h2>
          {chargesLoading && <Skeleton className="h-32 w-full rounded-lg" />}
          {!chargesLoading && charges?.length === 0 && (
            <p className="text-sm text-muted-foreground py-6 text-center">No booth-rent charges yet.</p>
          )}
          {!chargesLoading && charges && charges.length > 0 && (
            <DataTable<BoothRentChargeResponse>
              columns={[
                { header: "Artist", cell: (c) => c.artistName ?? "—" },
                { header: "Amount", cell: (c) => <span className="font-semibold">{formatCurrency(c.amount)}</span> },
                { header: "Charged", cell: (c) => formatDate(c.chargedDate) },
                { header: "Status", cell: (c) => <SettleButton charge={c} /> },
              ]}
              data={charges}
              keyExtractor={(c) => c.id}
              mobileCard={(c) => (
                <div className="space-y-1">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-sm font-medium">{c.artistName ?? "—"}</span>
                    <span className="font-semibold text-sm">{formatCurrency(c.amount)}</span>
                  </div>
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs text-muted-foreground">{formatDate(c.chargedDate)}</span>
                    <SettleButton charge={c} />
                  </div>
                </div>
              )}
            />
          )}
        </section>
      </main>
    </div>
  );
}
