import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import {
  ArrowLeft,
  Calendar,
  Clock,
  Loader2,
  Pencil,
  Trash2,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetServiceByIdQuery,
  useUpdateServiceMutation,
  useDeleteServiceMutation,
} from "../servicesApi";
import type { UpdateServiceRequest } from "../service.types";

const editSchema = z.object({
  name:            z.string().min(1, "Name is required").max(100, "Max 100 characters"),
  description:     z.string().max(2000, "Max 2000 characters").nullable(),
  durationMinutes: z.number({ error: "Duration is required" }).int().min(5, "Must be at least 5 minutes").max(600, "Must be 600 minutes or less"),
  price:           z.number().min(0, "Must be 0 or greater").nullable(),
  depositAmount:   z.number().min(0, "Must be 0 or greater").nullable(),
  isActive:        z.boolean(),
});

type EditFormValues = z.infer<typeof editSchema>;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function formatEuro(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

export function ServiceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const canManage = usePermission(Role.Owner);

  const { data: service, isLoading, isError } = useGetServiceByIdQuery(id!);
  const [updateService, { isLoading: isSaving }] = useUpdateServiceMutation();
  const [deleteService, { isLoading: isDeleting }] = useDeleteServiceMutation();

  const [mode, setMode] = useState<"view" | "edit" | "confirm-delete">("view");

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<EditFormValues>({ resolver: zodResolver(editSchema) });

  function startEdit() {
    if (!service) return;
    reset({
      name:            service.name,
      description:     service.description,
      durationMinutes: service.durationMinutes,
      price:           service.price,
      depositAmount:   service.depositAmount,
      isActive:        service.isActive,
    });
    setMode("edit");
  }

  async function onSave(values: EditFormValues) {
    if (!id) return;
    const body: UpdateServiceRequest = {
      name:            values.name,
      description:     values.description || null,
      durationMinutes: values.durationMinutes,
      price:           values.price,
      depositAmount:   values.depositAmount,
      isActive:        values.isActive,
    };
    const result = await updateService({ id, body });
    if ("data" in result) {
      toast.success("Service updated.");
      setMode("view");
    } else {
      toast.error("Failed to update service.");
    }
  }

  async function onDelete() {
    if (!id) return;
    const result = await deleteService(id);
    if ("error" in result) {
      toast.error("Failed to delete service.");
      return;
    }
    navigate("/services");
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background" aria-label="Loading service">
        <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
          <Skeleton className="h-8 w-32" />
        </header>
        <main className="max-w-lg mx-auto px-4 py-8 space-y-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="space-y-1.5">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-10 w-full rounded-md" />
            </div>
          ))}
        </main>
      </div>
    );
  }

  if (isError || !service) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive-text">Service not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/services")}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back to Services
        </Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/services")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Services
        </Button>

        {canManage && mode === "view" && (
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setMode("confirm-delete")}
              className="gap-1.5 text-destructive-text hover:text-destructive-text"
            >
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          </div>
        )}

        {mode === "edit" && (
          <Button variant="ghost" size="sm" onClick={() => setMode("view")} disabled={isSaving}>
            Cancel
          </Button>
        )}
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        {mode === "view" && (
          <>
            <div className="flex items-center gap-4">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-blue-500/10 text-blue-600">
                <Clock className="h-6 w-6" />
              </div>
              <div className="space-y-1.5">
                <h1 className="text-lg font-semibold leading-tight">{service.name}</h1>
                <span className={cn(
                  "text-xs px-2 py-0.5 rounded-full font-medium",
                  service.isActive ? "bg-green-500/10 text-green-700" : "bg-muted text-muted-foreground",
                )}>
                  {service.isActive ? "Active" : "Inactive"}
                </span>
              </div>
            </div>

            <Card>
              <CardContent className="p-4 space-y-3">
                {service.description && (
                  <p className="text-sm text-foreground">{service.description}</p>
                )}
                <div className="flex items-center gap-2 text-sm">
                  <Clock className="h-4 w-4 shrink-0 text-muted-foreground" />
                  <span>{service.durationMinutes} minutes</span>
                </div>
                <div className="text-xs text-muted-foreground pt-1 border-t space-y-1">
                  <p>
                    Price: {service.price !== null ? `from ${formatEuro(service.price)}` : "not shown"}
                  </p>
                  <p>
                    Deposit: {service.depositAmount !== null
                      ? `${formatEuro(service.depositAmount)} (overrides the studio's deposit rule)`
                      : "uses the studio's deposit rule"}
                  </p>
                </div>
                <div className="flex items-center gap-2 text-xs text-muted-foreground pt-1 border-t">
                  <Calendar className="h-3.5 w-3.5 shrink-0" />
                  <span>Created {formatDate(service.createdAt)}</span>
                </div>
              </CardContent>
            </Card>

            {canManage && (
              <p className="text-xs text-muted-foreground text-center">
                Last updated {formatDate(service.updatedAt)}
              </p>
            )}
          </>
        )}

        {mode === "edit" && (
          <form onSubmit={handleSubmit(onSave)} className="space-y-5">
            <h2 className="text-base font-semibold">Edit Service</h2>

            <div className="space-y-1.5">
              <Label htmlFor="edit-name">Service name</Label>
              <Input
                id="edit-name"
                {...register("name")}
                className={cn(errors.name && "border-destructive")}
              />
              {errors.name && (
                <p className="text-xs text-destructive-text">{errors.name.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-description">Description (optional)</Label>
              <Textarea
                id="edit-description"
                {...register("description")}
                className={cn(errors.description && "border-destructive")}
              />
              {errors.description && (
                <p className="text-xs text-destructive-text">{errors.description.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-durationMinutes">Duration (minutes)</Label>
              <Input
                id="edit-durationMinutes"
                type="number"
                step="1"
                {...register("durationMinutes", { valueAsNumber: true })}
                className={cn(errors.durationMinutes && "border-destructive")}
              />
              {errors.durationMinutes && (
                <p className="text-xs text-destructive-text">{errors.durationMinutes.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-price">Starting price (€, optional)</Label>
              <Input
                id="edit-price"
                type="number"
                step="0.01"
                min="0"
                {...register("price", {
                  setValueAs: (v) => (v === "" || v === null || v === undefined ? null : Number(v)),
                })}
                className={cn(errors.price && "border-destructive")}
              />
              {errors.price && (
                <p className="text-xs text-destructive-text">{errors.price.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-depositAmount">Deposit (€, optional)</Label>
              <Input
                id="edit-depositAmount"
                type="number"
                step="0.01"
                min="0"
                {...register("depositAmount", {
                  setValueAs: (v) => (v === "" || v === null || v === undefined ? null : Number(v)),
                })}
                className={cn(errors.depositAmount && "border-destructive")}
              />
              {errors.depositAmount && (
                <p className="text-xs text-destructive-text">{errors.depositAmount.message}</p>
              )}
              <p className="text-xs text-muted-foreground">
                When set, this replaces the studio's deposit rule entirely for bookings that
                pick this service.
              </p>
            </div>

            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                id="edit-isActive"
                {...register("isActive")}
                className="h-4 w-4 rounded border-input accent-primary"
              />
              <span className="text-sm">Active</span>
            </label>

            <Button type="submit" className="w-full" disabled={isSaving}>
              {isSaving ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Saving…
                </>
              ) : (
                "Save Changes"
              )}
            </Button>
          </form>
        )}

        {mode === "confirm-delete" && (
          <Card>
            <CardContent className="p-5 space-y-4">
              <p className="text-sm font-medium">Delete "{service.name}"?</p>
              <p className="text-xs text-muted-foreground">
                Existing appointments that used this service keep their duration and deposit —
                only the service itself is removed from the catalog. This action cannot be
                undone.
              </p>
              <div className="flex gap-2">
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={isDeleting}
                  onClick={onDelete}
                  className="flex-1"
                >
                  {isDeleting ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Deleting…
                    </>
                  ) : (
                    "Delete"
                  )}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={isDeleting}
                  onClick={() => setMode("view")}
                  className="flex-1"
                >
                  Cancel
                </Button>
              </div>
            </CardContent>
          </Card>
        )}
      </main>
    </div>
  );
}
