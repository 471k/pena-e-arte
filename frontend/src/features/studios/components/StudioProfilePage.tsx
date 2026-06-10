import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Building2, Loader2, Save } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery, useUpdateMyStudioMutation } from "../studiosApi";
import { BrandingSettingsCard } from "./BrandingSettingsCard";

const schema = z.object({
  name:      z.string().min(1, "Name is required").max(200),
  city:      z.string().min(1, "City is required").max(200),
  latitude:  z.number({ invalid_type_error: "Must be a number" }).min(-90).max(90),
  longitude: z.number({ invalid_type_error: "Must be a number" }).min(-180).max(180),
});

type FormValues = z.infer<typeof schema>;

export function StudioProfilePage() {
  const { data: studio, isLoading } = useGetMyStudioQuery();
  const [updateStudio, { isLoading: saving, isSuccess }] = useUpdateMyStudioMutation();

  const { register, handleSubmit, reset, formState: { errors, isDirty } } =
    useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (studio) {
      reset({
        name:      studio.name,
        city:      studio.city,
        latitude:  studio.latitude,
        longitude: studio.longitude,
      });
    }
  }, [studio, reset]);

  async function onSubmit(values: FormValues) {
    await updateStudio(values).unwrap();
    reset(values);
  }

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading…</span>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Studio Profile</span>
      </header>

      <main className="max-w-lg mx-auto px-4 py-6 space-y-4">
        {studio && (
          <Card>
            <CardContent className="py-3 px-4 text-sm text-muted-foreground">
              <span className="font-mono text-xs">{studio.slug}</span>
              {" · "}
              Registered {new Date(studio.createdAt).toLocaleDateString("en-GB")}
            </CardContent>
          </Card>
        )}

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Studio details</CardTitle>
          </CardHeader>
          <CardContent>
            {isSuccess && (
              <p className="text-sm text-green-600 mb-4">Changes saved.</p>
            )}
            <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="name">Studio name</Label>
                <Input id="name" {...register("name")} aria-invalid={!!errors.name} />
                {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="city">City</Label>
                <Input id="city" {...register("city")} aria-invalid={!!errors.city} />
                {errors.city && <p className="text-xs text-destructive">{errors.city.message}</p>}
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1.5">
                  <Label htmlFor="latitude">Latitude</Label>
                  <Input
                    id="latitude"
                    type="number"
                    step="any"
                    {...register("latitude", { valueAsNumber: true })}
                    aria-invalid={!!errors.latitude}
                  />
                  {errors.latitude && <p className="text-xs text-destructive">{errors.latitude.message}</p>}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="longitude">Longitude</Label>
                  <Input
                    id="longitude"
                    type="number"
                    step="any"
                    {...register("longitude", { valueAsNumber: true })}
                    aria-invalid={!!errors.longitude}
                  />
                  {errors.longitude && <p className="text-xs text-destructive">{errors.longitude.message}</p>}
                </div>
              </div>

              <Button
                type="submit"
                className="w-full gap-2"
                disabled={saving || !isDirty}
              >
                {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                Save changes
              </Button>
            </form>
          </CardContent>
        </Card>

        <BrandingSettingsCard />
      </main>
    </div>
  );
}
