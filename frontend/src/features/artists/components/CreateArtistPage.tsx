import { useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { SubscriptionGatedButton } from "@/shared/components/SubscriptionGatedButton";
import { useCreateArtistMutation } from "../artistsApi";

const createSchema = z.object({
  firstName:       z.string().min(1, "First name is required"),
  lastName:        z.string().min(1, "Last name is required"),
  email:           z.string().email("Invalid email"),
  specializations: z.string().optional(),
  hourlyRate:      z.number({ message: "Must be a number" }).positive("Must be positive").max(10_000).optional(),
});

type CreateFormValues = z.infer<typeof createSchema>;

export function CreateArtistPage() {
  const navigate = useNavigate();
  const [createArtist, { isLoading }] = useCreateArtistMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CreateFormValues>({ resolver: zodResolver(createSchema) });

  async function onSubmit(values: CreateFormValues) {
    const result = await createArtist({
      firstName:       values.firstName,
      lastName:        values.lastName,
      email:           values.email,
      specializations: values.specializations?.trim() || null,
      hourlyRate:      values.hourlyRate ?? null,
    });
    if ("data" in result) {
      toast.success("Artist created.");
      navigate(`/artists/${result.data!.id}`);
    } else {
      const errMsg =
        (result.error as { data?: { message?: string } } | undefined)?.data?.message
        ?? "Failed to create artist.";
      toast.error(errMsg);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/artists")}
          className="gap-1.5"
          disabled={isLoading}
        >
          <ArrowLeft className="h-4 w-4" />
          Artists
        </Button>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <h2 className="text-base font-semibold">New Artist</h2>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="firstName">First name</Label>
              <Input
                id="firstName"
                {...register("firstName")}
                className={cn(errors.firstName && "border-destructive")}
              />
              {errors.firstName && (
                <p className="text-xs text-destructive">{errors.firstName.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="lastName">Last name</Label>
              <Input
                id="lastName"
                {...register("lastName")}
                className={cn(errors.lastName && "border-destructive")}
              />
              {errors.lastName && (
                <p className="text-xs text-destructive">{errors.lastName.message}</p>
              )}
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              type="email"
              {...register("email")}
              className={cn(errors.email && "border-destructive")}
            />
            {errors.email && (
              <p className="text-xs text-destructive">{errors.email.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="specializations">Specializations (optional)</Label>
            <Input
              id="specializations"
              placeholder="e.g. Traditional, Realism"
              {...register("specializations")}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="hourlyRate">Hourly rate (€, optional)</Label>
            <Input
              id="hourlyRate"
              type="number"
              step="0.01"
              min="0"
              placeholder="e.g. 90"
              {...register("hourlyRate", { setValueAs: (v) => (v === "" || v == null ? undefined : Number(v)) })}
              className={cn(errors.hourlyRate && "border-destructive")}
            />
            <p className="text-xs text-muted-foreground">
              Used to calculate percentage-based booking deposits.
            </p>
            {errors.hourlyRate && (
              <p className="text-xs text-destructive">{errors.hourlyRate.message}</p>
            )}
          </div>

          <SubscriptionGatedButton type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Creating…
              </>
            ) : (
              "Create Artist"
            )}
          </SubscriptionGatedButton>
        </form>
      </main>
    </div>
  );
}
