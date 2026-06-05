import { useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetClientsQuery } from "@/features/clients/clientsApi";
import { useCreateDesignMutation } from "../designsApi";

const SELECT_CLS = cn(
  "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background focus-visible:outline-none focus-visible:ring-2",
  "focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50"
);

const TEXTAREA_CLS = cn(
  "flex min-h-[120px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background placeholder:text-muted-foreground",
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50 resize-none"
);

const createSchema = z.object({
  clientId:    z.string().min(1, "Select a client"),
  artistId:    z.string().min(1, "Select an artist"),
  title:       z.string().min(1, "Title is required").max(200, "Max 200 characters"),
  description: z.string().max(2000, "Max 2000 characters").optional(),
});

type FormValues = z.infer<typeof createSchema>;

export function CreateDesignPage() {
  const navigate = useNavigate();
  const { data: artists, isLoading: loadingArtists, isError: artistsError } = useGetArtistsQuery(undefined);
  const { data: clients, isLoading: loadingClients, isError: clientsError } = useGetClientsQuery(undefined);
  const [createDesign, { isLoading }] = useCreateDesignMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(createSchema) });

  async function onSubmit(values: FormValues) {
    const result = await createDesign({
      clientId:    values.clientId,
      artistId:    values.artistId,
      title:       values.title,
      description: values.description?.trim() || null,
    });
    if ("data" in result) {
      navigate("/designs");
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/designs")}
          className="gap-1.5"
          disabled={isLoading}
        >
          <ArrowLeft className="h-4 w-4" />
          Designs
        </Button>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <h2 className="text-base font-semibold">New Design</h2>

          <div className="space-y-1.5">
            <Label htmlFor="clientId">Client</Label>
            <select
              id="clientId"
              disabled={loadingClients || clientsError}
              {...register("clientId")}
              className={cn(SELECT_CLS, errors.clientId && "border-destructive")}
            >
              <option value="">
                {loadingClients ? "Loading…" : clientsError ? "Failed to load clients" : "Select a client"}
              </option>
              {clients?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.firstName} {c.lastName}
                </option>
              ))}
            </select>
            {errors.clientId && (
              <p className="text-xs text-destructive">{errors.clientId.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="artistId">Artist</Label>
            <select
              id="artistId"
              disabled={loadingArtists || artistsError}
              {...register("artistId")}
              className={cn(SELECT_CLS, errors.artistId && "border-destructive")}
            >
              <option value="">
                {loadingArtists ? "Loading…" : artistsError ? "Failed to load artists" : "Select an artist"}
              </option>
              {artists?.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.firstName} {a.lastName}
                </option>
              ))}
            </select>
            {errors.artistId && (
              <p className="text-xs text-destructive">{errors.artistId.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="title">Title</Label>
            <Input
              id="title"
              placeholder="e.g. Japanese sleeve concept"
              {...register("title")}
              className={cn(errors.title && "border-destructive")}
            />
            {errors.title && (
              <p className="text-xs text-destructive">{errors.title.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="description">Description (optional)</Label>
            <textarea
              id="description"
              rows={5}
              placeholder="Describe the concept, style, placement…"
              {...register("description")}
              className={TEXTAREA_CLS}
            />
            {errors.description && (
              <p className="text-xs text-destructive">{errors.description.message}</p>
            )}
          </div>

          <Button type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Creating…
              </>
            ) : (
              "Create Design"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
