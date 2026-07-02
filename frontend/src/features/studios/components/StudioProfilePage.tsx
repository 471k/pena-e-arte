import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Building2, Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { LocationPicker } from "@/shared/components/ui/location-picker";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { SubscriptionGatedButton } from "@/shared/components/SubscriptionGatedButton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetMyStudioQuery, useUpdateMyStudioMutation, useUpdateStudioSlugMutation } from "../studiosApi";
import { BrandingSettingsCard } from "./BrandingSettingsCard";
import { QrCodeSection } from "./QrCodeSection";
import { ReferralCodeCard } from "./ReferralCodeCard";
import { NotificationPreferencesCard } from "@/features/notifications/components/NotificationPreferencesCard";
import { EmbedCodeCard } from "./EmbedCodeCard";

const schema = z.object({
  name:            z.string().min(1, "Name is required").max(200),
  city:            z.string().min(1, "City is required").max(200),
  latitude:        z.number({ message: "Must be a number" }).min(-90).max(90),
  longitude:       z.number({ message: "Must be a number" }).min(-180).max(180),
  phoneNumber:     z.string().max(30, "Max 30 characters").optional(),
  instagramHandle: z.string().max(60, "Max 60 characters").optional(),
});

type FormValues = z.infer<typeof schema>;

function StudioProfileSkeleton() {
  return (
    <div className="min-h-screen bg-background" aria-label="Loading studio settings">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5 text-muted-foreground" />
        <Skeleton className="h-5 w-32" />
      </header>
      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        <div className="rounded-xl border bg-card p-3">
          <Skeleton className="h-4 w-64" />
        </div>
        <div className="rounded-xl border bg-card p-5 space-y-4">
          <Skeleton className="h-5 w-28" />
          <div className="space-y-1.5">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-10 w-full rounded-md" />
          </div>
          <div className="space-y-1.5">
            <Skeleton className="h-4 w-16" />
            <Skeleton className="h-48 w-full rounded-md" />
          </div>
          <Skeleton className="h-9 w-full rounded-md" />
        </div>
      </main>
    </div>
  );
}

function validateSlug(value: string): string | null {
  if (!value)                        return "Slug is required.";
  if (value.length > 60)             return "Slug must be 60 characters or fewer.";
  if (!/^[a-z0-9-]+$/.test(value))   return "Slug may only contain lowercase letters, numbers, and hyphens.";
  return null;
}

export function StudioProfilePage() {
  useDocumentMeta({ title: "Studio Settings — Pena e Artë", canonical: "/studios/me" });

  const { data: studio, isLoading } = useGetMyStudioQuery();
  const [updateStudio, { isLoading: saving, isSuccess }] = useUpdateMyStudioMutation();
  const [serverError, setServerError] = useState<string | null>(null);

  const [slugEditing, setSlugEditing] = useState(false);
  const [slugInput,   setSlugInput]   = useState("");
  const [slugError,   setSlugError]   = useState<string | null>(null);
  const [updateStudioSlug, { isLoading: slugSaving }] = useUpdateStudioSlugMutation();

  async function handleSlugSave() {
    const err = validateSlug(slugInput);
    if (err) { setSlugError(err); return; }
    setSlugError(null);
    try {
      await updateStudioSlug({ id: studio!.id, newSlug: slugInput }).unwrap();
      toast.success("Studio URL updated.");
      setSlugEditing(false);
    } catch (e: unknown) {
      const msg =
        e && typeof e === "object" && "data" in e &&
        (e as { data?: unknown }).data && typeof (e as { data?: unknown }).data === "object" &&
        "message" in ((e as { data?: unknown }).data as object)
          ? String(((e as { data: { message: string } }).data).message)
          : "Failed to update slug.";
      setSlugError(msg);
    }
  }

  const { register, handleSubmit, reset, watch, setValue, formState: { errors, isDirty } } =
    useForm<FormValues>({ resolver: zodResolver(schema) });

  const latValue  = watch("latitude");
  const lngValue  = watch("longitude");
  const cityValue = watch("city");

  useEffect(() => {
    if (studio) {
      reset({
        name:            studio.name,
        city:            studio.city,
        latitude:        studio.latitude,
        longitude:       studio.longitude,
        phoneNumber:     studio.phoneNumber ?? "",
        instagramHandle: studio.instagramHandle ?? "",
      });
    }
  }, [studio, reset]);

  async function onSubmit(values: FormValues) {
    setServerError(null);
    try {
      await updateStudio(values).unwrap();
      reset(values);
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "data" in err &&
        err.data && typeof err.data === "object" && "message" in err.data
          ? String((err.data as { message: string }).message)
          : "Unable to save changes.";
      setServerError(msg);
    }
  }

  if (isLoading) {
    return <StudioProfileSkeleton />;
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Studio Settings</span>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        {studio && (
          <Card>
            <CardContent className="py-3 px-4 space-y-2">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-xs font-semibold text-foreground">Studio URL:</span>
                {!slugEditing ? (
                  <>
                    <span className="font-mono text-xs text-foreground/80">{studio.slug}</span>
                    {studio.slugLockedAt ? (
                      <span className="text-xs text-muted-foreground italic ml-1">
                        · locked
                      </span>
                    ) : (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-6 px-2 text-xs"
                        onClick={() => { setSlugInput(studio.slug); setSlugEditing(true); setSlugError(null); }}
                      >
                        Edit
                      </Button>
                    )}
                  </>
                ) : (
                  <div className="flex items-center gap-2 flex-1 min-w-0">
                    <Input
                      value={slugInput}
                      onChange={(e) => { setSlugInput(e.target.value.toLowerCase()); setSlugError(null); }}
                      className="h-7 text-xs font-mono w-48"
                      placeholder="my-studio-slug"
                      maxLength={60}
                      aria-label="New studio URL slug"
                      aria-invalid={!!slugError}
                      aria-describedby={slugError ? "slug-error" : undefined}
                    />
                    <Button
                      size="sm"
                      className="h-7 text-xs"
                      onClick={handleSlugSave}
                      disabled={slugSaving || !slugInput}
                    >
                      {slugSaving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Save"}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-7 text-xs"
                      onClick={() => setSlugEditing(false)}
                      disabled={slugSaving}
                    >
                      Cancel
                    </Button>
                  </div>
                )}
                <span className="text-foreground/40">·</span>
                <span className="text-xs text-foreground/70">
                  Registered {new Date(studio.createdAt).toLocaleDateString("en-GB")}
                </span>
              </div>

              {slugError && (
                <p id="slug-error" className="text-xs text-destructive">{slugError}</p>
              )}

              {studio.slugLockedAt && (
                <p className="text-xs text-muted-foreground">
                  Studio URL was changed on {new Date(studio.slugLockedAt).toLocaleDateString("en-GB")}.
                  URLs can only be changed once.
                </p>
              )}
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
            {serverError && (
              <p className="text-sm text-destructive mb-4">{serverError}</p>
            )}
            <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="name">Studio name</Label>
                <Input id="name" {...register("name")} aria-invalid={!!errors.name} />
                {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="phoneNumber">Phone number (optional)</Label>
                <Input
                  id="phoneNumber"
                  type="tel"
                  placeholder="+351 912 345 678"
                  {...register("phoneNumber")}
                  aria-invalid={!!errors.phoneNumber}
                  aria-describedby={errors.phoneNumber ? "phoneNumber-error" : undefined}
                />
                {errors.phoneNumber && (
                  <p id="phoneNumber-error" className="text-xs text-destructive">{errors.phoneNumber.message}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="instagramHandle">Instagram handle (optional)</Label>
                <Input
                  id="instagramHandle"
                  placeholder="your_studio"
                  {...register("instagramHandle")}
                  aria-invalid={!!errors.instagramHandle}
                  aria-describedby={errors.instagramHandle ? "instagramHandle-error" : undefined}
                />
                {errors.instagramHandle && (
                  <p id="instagramHandle-error" className="text-xs text-destructive">{errors.instagramHandle.message}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <Label>Location</Label>
                <p className="text-xs text-muted-foreground">
                  Click the map or drag the pin to update your studio location.
                </p>
                {/* key forces remount once studio data arrives so the pin initialises correctly */}
                <LocationPicker
                  key={studio ? `${studio.latitude},${studio.longitude}` : "unset"}
                  value={
                    latValue != null && !isNaN(latValue) && lngValue != null && !isNaN(lngValue)
                      ? { lat: latValue, lng: lngValue, city: cityValue ?? "" }
                      : undefined
                  }
                  onChange={({ lat, lng, city }) => {
                    setValue("latitude",  lat,  { shouldDirty: true, shouldValidate: true });
                    setValue("longitude", lng,  { shouldDirty: true, shouldValidate: true });
                    setValue("city",      city, { shouldDirty: true, shouldValidate: true });
                  }}
                  error={
                    errors.latitude?.message ??
                    errors.longitude?.message ??
                    errors.city?.message
                  }
                />
              </div>

              <SubscriptionGatedButton
                type="submit"
                className="w-full gap-2"
                disabled={saving || !isDirty}
              >
                {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                Save changes
              </SubscriptionGatedButton>
            </form>
          </CardContent>
        </Card>

        <BrandingSettingsCard />
        <QrCodeSection />
        <EmbedCodeCard />
        <ReferralCodeCard />
        <NotificationPreferencesCard />
      </main>
    </div>
  );
}
