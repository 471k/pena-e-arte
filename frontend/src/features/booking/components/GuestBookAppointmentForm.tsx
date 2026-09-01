import { useMemo, useState } from "react";
import { useForm, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertCircle, CheckCircle2, Loader2 } from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Input }    from "@/shared/components/ui/input";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { isValidE164Phone, PHONE_ERROR_MESSAGE } from "@/shared/utils/phoneValidation";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { cn } from "@/shared/utils/cn";
import { toLocalDatetimeInputValue } from "@/shared/utils/localDatetimeInput";
import { useCategorizedImageUpload } from "@/shared/hooks/useCategorizedImageUpload";
import { useDebouncedSlotCheckArgs } from "@/shared/hooks/useDebouncedSlotCheckArgs";
import {
  useGetPublicBookingArtistsQuery,
  useCheckPublicSlotAvailabilityQuery,
  useGetPublicDepositRuleQuery,
  useCreateGuestAppointmentMutation,
  usePresignGuestUploadMutation,
} from "../../public/publicApi";
import { FieldLabel } from "@/features/appointments/components/FieldLabel";
import { TattooIntakeFields } from "@/features/appointments/components/TattooIntakeFields";
import { validateTattooIntake, type TattooIntakeValues } from "@/features/appointments/components/tattooIntakeValidation";
import { CategorizedImagesField } from "@/features/appointments/components/CategorizedImagesField";
import { DesiredPlacementField } from "@/features/appointments/components/DesiredPlacementField";
import { SlotAvailabilityIndicator } from "@/features/appointments/components/SlotAvailabilityIndicator";
import { AppointmentAttachmentCategory } from "@/features/appointments/appointment.types";

const VALID_DURATIONS = [30, 45, 60, 90, 120, 180, 240, 300, 360, 480] as const;
// Mirrors GetPresignedGuestUploadUrlValidator's accepted content types.
const MAX_IMAGES = 6;

const DURATION_OPTIONS: { value: number; label: string }[] = [
  { value: 30,  label: "30 min — Touch-up" },
  { value: 45,  label: "45 min" },
  { value: 60,  label: "1 hour" },
  { value: 90,  label: "1.5 hours" },
  { value: 120, label: "2 hours" },
  { value: 180, label: "3 hours" },
  { value: 240, label: "4 hours" },
  { value: 300, label: "5 hours" },
  { value: 360, label: "6 hours" },
  { value: 480, label: "Full day (8 h)" },
];

const schema = z.object({
  firstName:       z.string().min(1, "First name is required"),
  lastName:        z.string().min(1, "Last name is required"),
  email:           z.string().min(1, "Email is required").email("Invalid email"),
  marketingOptIn:  z.boolean(),
  phone:           z.string().min(1, "Phone number is required").refine(isValidE164Phone, PHONE_ERROR_MESSAGE),
  artistId:        z.string().nullable(),
  bookAnyArtist:   z.boolean(),
  scheduledAt:     z.string().min(1, "Select date and time").refine(
    (v) => new Date(v) > new Date(),
    "Appointment must be in the future",
  ),
  durationMinutes: z.number().refine(
    (v) => (VALID_DURATIONS as readonly number[]).includes(v),
    "Select a valid appointment duration",
  ),
  notes: z.string().optional(),
}).refine(
  (data) => data.bookAnyArtist || (!!data.artistId && data.artistId.length > 0),
  { message: "Select an artist", path: ["artistId"] },
);

type FormValues = z.infer<typeof schema>;

// Builds the shared useCategorizedImageUpload hook's `upload` function against the anonymous
// guest presign endpoint (Decision #10: image-only content types, server-constructed key, no
// client-supplied prefix) instead of the authenticated files/presign one BookAppointmentForm
// uses. The queue/preview/status state itself is no longer duplicated — see
// shared/hooks/useCategorizedImageUpload.ts. Found via /code-review, 2026-09-01.
function useGuestPresignUpload() {
  const [presign] = usePresignGuestUploadMutation();

  return function buildUpload(slug: string, category: "area" | "reference") {
    return async (file: File) => {
      try {
        const result = await presign({ slug, contentType: file.type, category }).unwrap();
        const putResp = await fetch(result.uploadUrl, {
          method:  "PUT",
          body:    file,
          headers: { "Content-Type": file.type },
        });
        return putResp.ok ? result.publicUrl : null;
      } catch {
        return null;
      }
    };
  };
}

function DepositPreview({
  durationMinutes,
  hourlyRate,
  rule,
}: {
  durationMinutes: number;
  hourlyRate:       number | null;
  rule:             { name: string; amountFixed: number | null; amountPercent: number | null };
}) {
  let estimated: number | null = null;
  if (rule.amountFixed !== null) {
    estimated = rule.amountFixed;
  } else if (rule.amountPercent !== null && hourlyRate !== null) {
    const sessionHours = durationMinutes / 60;
    estimated = (sessionHours * hourlyRate * rule.amountPercent) / 100;
  }
  if (estimated === null) return null;

  return (
    <div className="flex items-center justify-between rounded-md
                    bg-muted/40 border border-border/30 px-3 py-2">
      <span className="text-xs text-muted-foreground">Estimated deposit</span>
      <span className="text-sm font-semibold tabular-nums">€{estimated.toFixed(2)}</span>
    </div>
  );
}

interface GuestBookAppointmentFormProps {
  slug: string;
}

/** Unauthenticated booking form for a visitor with no account yet (Decision #1 — true guest
 *  checkout). Identity fields (name/email/phone/marketing opt-in) are guest-only; the booking-
 *  content fields below are shared with the authenticated BookAppointmentForm (Decision #8). */
export function GuestBookAppointmentForm({ slug }: GuestBookAppointmentFormProps) {
  const { data: artists, isLoading: loadingArtists } = useGetPublicBookingArtistsQuery(slug);
  const { data: depositRule } = useGetPublicDepositRuleQuery(slug);
  const [createGuestAppointment, { isLoading: submitting }] = useCreateGuestAppointmentMutation();

  const [booked, setBooked] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const buildGuestUpload = useGuestPresignUpload();
  const areaPhotos = useCategorizedImageUpload({
    maxImages: MAX_IMAGES,
    upload:    buildGuestUpload(slug, "area"),
  });
  const referenceImages = useCategorizedImageUpload({
    maxImages: MAX_IMAGES,
    upload:    buildGuestUpload(slug, "reference"),
  });
  const anyImageUploading = areaPhotos.uploading || referenceImages.uploading;

  const [intake, setIntake] = useState<TattooIntakeValues>({
    tattooDescription: "", referralSource: "", referralSourceOther: "", safetyNotes: "",
  });
  const [tattooDescriptionError, setTattooDescriptionError] = useState<string | null>(null);
  const [referralSourceOtherError, setReferralSourceOtherError] = useState<string | null>(null);
  const [desiredPlacement, setDesiredPlacement] = useState<string[]>([]);
  const [areaPhotoError, setAreaPhotoError] = useState<string | null>(null);
  const [referenceImageError, setReferenceImageError] = useState<string | null>(null);

  const {
    register, control, handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      firstName: "", lastName: "", email: "", phone: "", marketingOptIn: false,
      artistId: "", bookAnyArtist: false, durationMinutes: 60,
    },
  });

  const watchedArtistId      = useWatch({ control, name: "artistId" });
  const watchedBookAnyArtist = useWatch({ control, name: "bookAnyArtist" });
  const watchedDate          = useWatch({ control, name: "scheduledAt" });
  const watchedDuration      = useWatch({ control, name: "durationMinutes" });

  const selectedArtist = useMemo(
    () => (artists ?? []).find((a) => a.artistId === watchedArtistId) ?? null,
    [artists, watchedArtistId],
  );

  const debouncedCheck = useDebouncedSlotCheckArgs(
    watchedArtistId, watchedBookAnyArtist, watchedDate, watchedDuration,
  );

  const { data: slotStatus, isFetching: checkingSlot } = useCheckPublicSlotAvailabilityQuery(
    debouncedCheck ? { slug, ...debouncedCheck } : { slug, date: "", durationMinutes: 0 },
    { skip: debouncedCheck === null },
  );

  async function onSubmit(values: FormValues) {
    setSubmitError(null);
    setAreaPhotoError(null);
    setReferenceImageError(null);

    const { tattooDescriptionError, referralSourceOtherError } = validateTattooIntake(intake);
    setTattooDescriptionError(tattooDescriptionError);
    setReferralSourceOtherError(referralSourceOtherError);

    let valid = !tattooDescriptionError && !referralSourceOtherError;
    if (areaPhotos.doneUrls().length === 0) {
      setAreaPhotoError("A photo of the area is required.");
      valid = false;
    }
    if (referenceImages.doneUrls().length === 0) {
      setReferenceImageError("At least one reference image is required.");
      valid = false;
    }
    if (!valid) return;

    const images = [
      ...areaPhotos.doneUrls().map((url) => ({ url, category: AppointmentAttachmentCategory.AreaPhoto })),
      ...referenceImages.doneUrls().map((url) => ({ url, category: AppointmentAttachmentCategory.Reference })),
    ];

    const result = await createGuestAppointment({
      slug,
      body: {
        firstName:      values.firstName,
        lastName:       values.lastName,
        email:          values.email,
        phone:          values.phone,
        marketingOptIn: values.marketingOptIn,
        booking: {
          artistId:        values.bookAnyArtist ? null : values.artistId,
          // Ignored by CreateGuestAppointmentHandler (it resolves the real client server-side) —
          // but the backend's ClientId is a non-nullable Guid, so this must still be one
          // syntactically valid GUID, not an empty string (which fails JSON→Guid deserialization
          // and 400s the whole request before the handler ever runs).
          clientId:        "00000000-0000-0000-0000-000000000000",
          date:            new Date(values.scheduledAt).toISOString(),
          durationMinutes: values.durationMinutes,
          notes:           values.notes || null,
          tattooDescription:         intake.tattooDescription,
          safetyNotes:               intake.safetyNotes || null,
          desiredPlacementLocations: desiredPlacement,
          referralSource:            intake.referralSource || null,
          referralSourceOther:       intake.referralSourceOther || null,
          images,
        },
      },
    });

    if ("data" in result) {
      // Backend intentionally responds identically here whether a new booking was created or
      // the email collided with an existing account (enumeration-resistance, 2026-09-01) — the
      // success screen's copy already covers both ("check your email"; an existing-account
      // guest gets a different email telling them to log in instead).
      setBooked(true);
    } else {
      const errMsg = (result.error as { data?: { message?: string } } | undefined)?.data?.message;
      setSubmitError(errMsg ?? "Failed to book appointment. Please try again.");
    }
  }

  if (booked) {
    // Deliberately generic — the backend responds identically whether a new booking was
    // created or the email already had an account (enumeration-resistance, 2026-09-01), so this
    // screen can't claim "Appointment requested!" as a certainty. The follow-up email
    // disambiguates: a booking-confirmation + set-password email for a genuinely new guest, or
    // a "log in to book" notice if the email already had an account.
    return (
      <div className="text-center space-y-3 py-6">
        <CheckCircle2 className="h-8 w-8 mx-auto text-green-500" aria-hidden="true" />
        <p className="text-sm font-medium">Check your email</p>
        <p className="text-xs text-muted-foreground max-w-sm mx-auto">
          We&apos;ve sent an email with next steps. If this is your first booking with us,
          it confirms your request and helps you set up your account. If you already have an
          account, it explains how to log in and book from there — you can also use
          &ldquo;Forgot password&rdquo; any time with this email address.
        </p>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
      <p className="text-xs text-muted-foreground/60">* Required</p>

      {submitError && (
        <div
          role="alert"
          className="flex items-center gap-2 rounded-md border border-destructive/30
                     bg-destructive/5 px-3 py-3 text-sm text-destructive"
        >
          <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
          {submitError}
        </div>
      )}

      {/* Identity */}
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <FieldLabel htmlFor="firstName" required>First name</FieldLabel>
          <Input id="firstName" {...register("firstName")}
                 className={cn(errors.firstName && "border-destructive")} />
          {errors.firstName && <p className="text-xs text-destructive" role="alert">{errors.firstName.message}</p>}
        </div>
        <div className="space-y-1.5">
          <FieldLabel htmlFor="lastName" required>Last name</FieldLabel>
          <Input id="lastName" {...register("lastName")}
                 className={cn(errors.lastName && "border-destructive")} />
          {errors.lastName && <p className="text-xs text-destructive" role="alert">{errors.lastName.message}</p>}
        </div>
      </div>

      <div className="space-y-1.5">
        <FieldLabel htmlFor="email" required>Email</FieldLabel>
        <Input id="email" type="email" {...register("email")}
               className={cn(errors.email && "border-destructive")} />
        {errors.email && <p className="text-xs text-destructive" role="alert">{errors.email.message}</p>}
      </div>

      <div className="flex items-center justify-between rounded-md border border-border/40
                      bg-muted/20 px-3 py-2">
        <p className="text-xs font-medium">Sign up for news and updates</p>
        <Controller
          control={control}
          name="marketingOptIn"
          render={({ field }) => (
            <ToggleSwitch
              checked={field.value}
              onChange={() => field.onChange(!field.value)}
              aria-label="Sign up for news and updates"
            />
          )}
        />
      </div>

      <div className="space-y-1.5">
        <FieldLabel htmlFor="phone" required>Phone</FieldLabel>
        <Controller
          control={control}
          name="phone"
          render={({ field }) => (
            <PhoneInput
              id="phone"
              value={field.value}
              onChange={field.onChange}
              onBlur={field.onBlur}
              aria-invalid={!!errors.phone}
            />
          )}
        />
        {errors.phone && <p className="text-xs text-destructive" role="alert">{errors.phone.message}</p>}
      </div>

      {/* Artist selector */}
      {!watchedBookAnyArtist && (
        <div className="space-y-1.5">
          <FieldLabel htmlFor="artistId" required>Artist</FieldLabel>
          <Controller
            control={control}
            name="artistId"
            render={({ field }) => (
              <Select disabled={loadingArtists} value={field.value ?? ""} onValueChange={field.onChange}>
                <SelectTrigger id="artistId" className={cn(errors.artistId && "border-destructive")}>
                  <SelectValue placeholder={loadingArtists ? "Loading artists…" : "Choose an artist"} />
                </SelectTrigger>
                <SelectContent>
                  {(artists ?? []).map((a) => (
                    <SelectItem key={a.artistId} value={a.artistId}>{a.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
          {errors.artistId && <p className="text-xs text-destructive" role="alert">{errors.artistId.message}</p>}
        </div>
      )}

      <div className="flex items-center justify-between rounded-md border border-border/40
                      bg-muted/20 px-3 py-2">
        <p className="text-xs font-medium">Let the studio choose my artist</p>
        <Controller
          control={control}
          name="bookAnyArtist"
          render={({ field }) => (
            <ToggleSwitch
              checked={field.value}
              onChange={() => field.onChange(!field.value)}
              aria-label="Let the studio choose my artist"
            />
          )}
        />
      </div>

      {/* Date & duration */}
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5 col-span-2 sm:col-span-1">
          <FieldLabel htmlFor="scheduledAt" required>Date &amp; Time</FieldLabel>
          <Input
            id="scheduledAt"
            type="datetime-local"
            min={toLocalDatetimeInputValue(new Date())}
            {...register("scheduledAt")}
            className={cn(errors.scheduledAt && "border-destructive")}
          />
          {errors.scheduledAt && (
            <p className="text-xs text-destructive" role="alert">{errors.scheduledAt.message}</p>
          )}
        </div>
        <div className="space-y-1.5 col-span-2 sm:col-span-1">
          <FieldLabel htmlFor="durationMinutes" required>Appointment Duration</FieldLabel>
          <Controller
            control={control}
            name="durationMinutes"
            render={({ field }) => (
              <Select value={String(field.value)} onValueChange={(v) => field.onChange(Number(v))}>
                <SelectTrigger id="durationMinutes" className={cn(errors.durationMinutes && "border-destructive")}>
                  <SelectValue placeholder="Select duration" />
                </SelectTrigger>
                <SelectContent>
                  {DURATION_OPTIONS.map(({ value, label }) => (
                    <SelectItem key={value} value={String(value)}>{label}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
          {errors.durationMinutes && (
            <p className="text-xs text-destructive" role="alert">{errors.durationMinutes.message}</p>
          )}
        </div>
      </div>

      {debouncedCheck !== null && (
        <SlotAvailabilityIndicator checking={checkingSlot} status={slotStatus} />
      )}

      {depositRule && watchedDuration > 0 && (
        <DepositPreview
          durationMinutes={watchedDuration}
          hourlyRate={selectedArtist?.hourlyRate ?? null}
          rule={depositRule}
        />
      )}

      {/* Tattoo description, referral source, safety notes */}
      <TattooIntakeFields
        value={intake}
        onChange={setIntake}
        tattooDescriptionError={tattooDescriptionError ?? undefined}
        referralSourceOtherError={referralSourceOtherError ?? undefined}
      />

      <DesiredPlacementField locations={desiredPlacement} onChange={setDesiredPlacement} />

      {/* Both required for guest checkout (Decision #6) — unlike the existing authenticated
          form, which keeps both optional (Part 6d note). */}
      <CategorizedImagesField
        category={AppointmentAttachmentCategory.AreaPhoto}
        label="Area photo"
        helperText={`Click to add a photo of the area — JPEG, PNG, or WebP (up to ${MAX_IMAGES})`}
        required
        max={MAX_IMAGES}
        images={areaPhotos.images}
        error={areaPhotos.error ?? areaPhotoError}
        onPick={(files) => void areaPhotos.pick(files)}
        onRemove={areaPhotos.remove}
        disabled={submitting}
      />
      <CategorizedImagesField
        category={AppointmentAttachmentCategory.Reference}
        label="Reference images"
        helperText={`Click to add photos — JPEG, PNG, or WebP (up to ${MAX_IMAGES})`}
        required
        max={MAX_IMAGES}
        images={referenceImages.images}
        error={referenceImages.error ?? referenceImageError}
        onPick={(files) => void referenceImages.pick(files)}
        onRemove={referenceImages.remove}
        disabled={submitting}
      />

      <Button
        type="submit"
        className="w-full bg-violet-600 hover:bg-violet-700 text-white font-medium
                   disabled:bg-violet-600/50"
        disabled={submitting || slotStatus?.available === false || anyImageUploading}
      >
        {submitting ? (
          <><Loader2 className="h-4 w-4 animate-spin mr-2" aria-hidden="true" />Booking…</>
        ) : anyImageUploading ? (
          <><Loader2 className="h-4 w-4 animate-spin mr-2" aria-hidden="true" />Uploading images…</>
        ) : (
          "Request Appointment"
        )}
      </Button>

      <p className="text-center text-[11px] text-muted-foreground/60">
        No account needed — we&apos;ll set one up for you and email you a link to manage this booking.
      </p>
    </form>
  );
}
