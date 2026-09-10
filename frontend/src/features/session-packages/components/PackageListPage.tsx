import { useState } from "react";
import { toast } from "sonner";
import { Package as PackageIcon, Plus, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Badge } from "@/shared/components/ui/badge";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogTrigger,
} from "@/shared/components/ui/dialog";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetPackagesQuery, useCreatePackageMutation, useUpdatePackageMutation } from "../packagesApi";
import type { PackageResponse } from "../packages.types";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function NewPackageDialog() {
  const [createPackage, { isLoading }] = useCreatePackageMutation();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [sessionCount, setSessionCount] = useState("");
  const [price, setPrice] = useState("");

  async function handleSubmit() {
    if (!name || !sessionCount || !price) return;
    const result = await createPackage({
      name, sessionCount: Number(sessionCount), price: Number(price), isActive: true,
    });
    if ("data" in result) {
      toast.success("Package created.");
      setOpen(false);
      setName(""); setSessionCount(""); setPrice("");
    } else {
      toast.error("Failed to create package.");
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button size="sm" className="gap-1.5"><Plus className="h-3.5 w-3.5" />New package</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader><DialogTitle>New package</DialogTitle></DialogHeader>
        <div className="space-y-3 py-2">
          <div className="space-y-1.5">
            <Label htmlFor="package-name">Name</Label>
            <Input id="package-name" placeholder="e.g. 5-Session Sleeve Package" value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="package-sessions">Sessions</Label>
            <Input id="package-sessions" type="number" min="1" step="1" value={sessionCount} onChange={(e) => setSessionCount(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="package-price">Price (€)</Label>
            <Input id="package-price" type="number" min="0.01" step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} />
          </div>
        </div>
        <DialogFooter>
          <Button onClick={handleSubmit} disabled={isLoading || !name || !sessionCount || !price} className="gap-1.5">
            {isLoading && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Create package
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Owner-only package management — same list+create+edit shape as the deposit rules page. */
export function PackageListPage() {
  useDocumentMeta({ title: "Packages — TattooOS", canonical: "/packages" });

  const { data: packages, isLoading, isError } = useGetPackagesQuery();
  const [updatePackage] = useUpdatePackageMutation();

  async function toggleActive(id: string, current: PackageResponse) {
    const result = await updatePackage({
      id,
      body: { name: current.name, sessionCount: current.sessionCount, price: current.price, isActive: !current.isActive },
    });
    if (!("data" in result)) toast.error("Failed to update package.");
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <PackageIcon className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Packages</span>
        </div>
        <NewPackageDialog />
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-2">
        {isLoading && (
          <div className="space-y-3" aria-label="Loading packages">
            {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-16 w-full rounded-lg" />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16">Failed to load packages.</p>
        )}

        {!isLoading && !isError && packages?.length === 0 && (
          <div className="flex flex-col items-center gap-2 py-20 text-center">
            <PackageIcon className="h-10 w-10 text-muted-foreground/50" />
            <p className="text-sm font-medium text-foreground">No packages yet</p>
            <p className="text-xs text-muted-foreground">
              Create a prepaid multi-session bundle for clients to purchase.
            </p>
          </div>
        )}

        {!isLoading && !isError && packages?.map((pkg) => (
          <div key={pkg.id} className="flex items-center justify-between gap-3 rounded-lg border p-3">
            <div>
              <p className="text-sm font-medium">{pkg.name}</p>
              <p className="text-xs text-muted-foreground">
                {pkg.sessionCount} sessions — {formatCurrency(pkg.price)}
              </p>
            </div>
            <Badge
              variant="outline"
              className={cnActive(pkg.isActive)}
              role="button"
              onClick={() => toggleActive(pkg.id, pkg)}
            >
              {pkg.isActive ? "Active" : "Inactive"}
            </Badge>
          </div>
        ))}
      </main>
    </div>
  );
}

function cnActive(active: boolean): string {
  return active
    ? "border-green-300 bg-green-100 text-green-800 cursor-pointer"
    : "border-slate-300 bg-slate-100 text-slate-800 cursor-pointer";
}
