import { useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { cn } from "@/shared/utils/cn";
import { useCreateServiceMutation } from "../servicesApi";
import type { CreateServiceRequest } from "../service.types";

const createSchema = z.object({
  name:            z.string().min(1, "Name is required").max(100, "Max 100 characters"),
  description:     z.string().max(2000, "Max 2000 characters").nullable(),
  durationMinutes: z.number({ error: "Duration is required" }).int().min(5, "Must be at least 5 minutes").max(600, "Must be 600 minutes or less"),
  price:           z.number().min(0, "Must be 0 or greater").nullable(),
  depositAmount:   z.number().min(0, "Must be 0 or greater").nullable(),
  isActive:        z.boolean(),
});

type CreateFormValues = z.infer<typeof createSchema>;

export function CreateServicePage() {
  const navigate = useNavigate();
  const [createService, { isLoading }] = useCreateServiceMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: {
      description: null,
      durationMinutes: 60,
      price: null,
      depositAmount: null,
      isActive: true,
    },
  });

  async function onSubmit(values: CreateFormValues) {
    const body: CreateServiceRequest = {
      name:            values.name,
      description:     values.description || null,
      durationMinutes: values.durationMinutes,
      price:           values.price,
      depositAmount:   values.depositAmount,
      isActive:        values.isActive,
    };
    const result = await createService(body);
    if ("data" in result) {
      toast.success("Service created.");
      navigate(`/services/${result.data!.id}`);
    } else {
      toast.error("Failed to create service.");
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/services")}
          className="gap-1.5"
          disabled={isLoading}
        >
          <ArrowLeft className="h-4 w-4" />
          Services
        </Button>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <h2 className="text-base font-semibold">New Service</h2>

          <div className="space-y-1.5">
            <Label htmlFor="name">Service name</Label>
            <Input
              id="name"
              placeholder="e.g. New Tattoo Session"
              {...register("name")}
              className={cn(errors.name && "border-destructive")}
            />
            {errors.name && (
              <p className="text-xs text-destructive-text">{errors.name.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="description">Description (optional)</Label>
            <Textarea
              id="description"
              placeholder="What's included in this service"
              {...register("description")}
              className={cn(errors.description && "border-destructive")}
            />
            {errors.description && (
              <p className="text-xs text-destructive-text">{errors.description.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="durationMinutes">Duration (minutes)</Label>
            <Input
              id="durationMinutes"
              type="number"
              step="1"
              {...register("durationMinutes", { valueAsNumber: true })}
              className={cn(errors.durationMinutes && "border-destructive")}
            />
            {errors.durationMinutes && (
              <p className="text-xs text-destructive-text">{errors.durationMinutes.message}</p>
            )}
            <p className="text-xs text-muted-foreground">
              A client who picks this service gets this duration automatically — they won't be
              able to change it.
            </p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="price">Starting price (€, optional)</Label>
            <Input
              id="price"
              type="number"
              step="0.01"
              min="0"
              placeholder="Shown to clients — not charged automatically"
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
            <Label htmlFor="depositAmount">Deposit (€, optional)</Label>
            <Input
              id="depositAmount"
              type="number"
              step="0.01"
              min="0"
              placeholder="Leave blank to use the studio's default deposit rule"
              {...register("depositAmount", {
                setValueAs: (v) => (v === "" || v === null || v === undefined ? null : Number(v)),
              })}
              className={cn(errors.depositAmount && "border-destructive")}
            />
            {errors.depositAmount && (
              <p className="text-xs text-destructive-text">{errors.depositAmount.message}</p>
            )}
            <p className="text-xs text-muted-foreground">
              When set, this replaces the studio's deposit rule entirely for bookings that pick
              this service.
            </p>
          </div>

          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              id="isActive"
              {...register("isActive")}
              className="h-4 w-4 rounded border-input accent-primary"
            />
            <span className="text-sm">Active</span>
          </label>

          <Button type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Creating…
              </>
            ) : (
              "Create Service"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
