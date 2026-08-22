import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useCreateClientMutation } from "../clientsApi";
import { useGetArtistsQuery, useGetMyArtistQuery } from "@/features/artists/artistsApi";

const createSchema = z.object({
  firstName: z.string().min(1, "First name is required"),
  lastName:  z.string().min(1, "Last name is required"),
  email:     z.string().email("Invalid email"),
  artistId:  z.string().min(1, "Select an artist"),
  phone:     z.string().optional(),
});

type CreateFormValues = z.infer<typeof createSchema>;

export function CreateClientPage() {
  const navigate = useNavigate();
  const [createClient, { isLoading }] = useCreateClientMutation();

  const isOwnerPlus = usePermission(Role.Owner);
  const { data: artists, isLoading: loadingArtists } = useGetArtistsQuery(undefined, { skip: !isOwnerPlus });
  const { data: myArtist } = useGetMyArtistQuery(undefined, { skip: isOwnerPlus });

  const {
    register,
    handleSubmit,
    control,
    setValue,
    formState: { errors },
  } = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { artistId: "" },
  });

  useEffect(() => {
    if (!isOwnerPlus && myArtist) setValue("artistId", myArtist.id);
  }, [isOwnerPlus, myArtist, setValue]);

  async function onSubmit(values: CreateFormValues) {
    const result = await createClient({
      firstName: values.firstName,
      lastName:  values.lastName,
      email:     values.email,
      phone:     values.phone?.trim() || null,
      artistId:  values.artistId,
    });
    if ("data" in result) {
      toast.success("Client created.");
      navigate(`/clients/${result.data!.id}`);
    } else {
      toast.error("Failed to create client.");
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/clients")}
          className="gap-1.5"
          disabled={isLoading}
        >
          <ArrowLeft className="h-4 w-4" />
          Clients
        </Button>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <h2 className="text-base font-semibold">New Client</h2>

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

          {isOwnerPlus && (
            <div className="space-y-1.5">
              <Label htmlFor="artistId">Artist</Label>
              <Controller
                control={control}
                name="artistId"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger
                      id="artistId"
                      aria-label="Select artist"
                      className={cn(errors.artistId && "border-destructive")}
                    >
                      <SelectValue placeholder={loadingArtists ? "Loading artists…" : "Choose an artist"} />
                    </SelectTrigger>
                    <SelectContent>
                      {artists?.map((a) => (
                        <SelectItem key={a.id} value={a.id}>
                          {a.firstName} {a.lastName}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {errors.artistId && (
                <p className="text-xs text-destructive">{errors.artistId.message}</p>
              )}
            </div>
          )}
          {!isOwnerPlus && <input type="hidden" {...register("artistId")} />}

          <div className="space-y-1.5">
            <Label htmlFor="phone">Phone (optional)</Label>
            <Input
              id="phone"
              type="tel"
              placeholder="e.g. +351 912 345 678"
              {...register("phone")}
            />
          </div>

          <Button
            type="submit"
            className="w-full"
            disabled={isLoading || (!isOwnerPlus && !myArtist)}
          >
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Creating…
              </>
            ) : (
              "Create Client"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
