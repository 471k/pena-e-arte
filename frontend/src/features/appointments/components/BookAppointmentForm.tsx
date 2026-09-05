import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useForm, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import * as SelectPrimitive from "@radix-ui/react-select";
import {
  AlertCircle, Banknote, Check, CheckCircle2, Loader2,
} from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Input }    from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import {
  Select, SelectContent, SelectItem,
  SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { useAppSelector }  from "@/app/hooks";
import { useCurrentUser }  from "@/shared/hooks/useCurrentUser";
import { usePresignedUpload } from "@/shared/hooks/usePresignedUpload";
import { useCategorizedImageUpload, ACCEPTED_IMAGE_TYPES } from "@/shared/hooks/useCategorizedImageUpload";
import { useDebouncedSlotCheckArgs } from "@/shared/hooks/useDebouncedSlotCheckArgs";
import { cn }              from "@/shared/utils/cn";
import { generateUuid }    from "@/shared/utils/uuid";
import { toLocalDatetimeInputValue } from "@/shared/utils/localDatetimeInput";
import { Role }            from "@/shared/types/roles";
import {
  useCreateAppointmentMutation,
  useCheckSlotAvailabilityQuery,
} from "../appointmentsApi";
import { useGetArtistsQuery }                     from "@/features/artists/artistsApi";
import { useGetClientsQuery, useGetMyClientQuery } from "@/features/clients/clientsApi";
import { useGetDepositRulesQuery }                from "@/features/deposit-rules/depositRulesApi";
import { useGetPublicStudioQuery }                from "@/features/public/publicApi";
import { useEnsureActiveStudio }                  from "@/features/auth/useEnsureActiveStudio";
import { PaymentMethodSelector }                  from "@/features/payments/components/PaymentMethodSelector";
import { SlotAvailabilityIndicator }              from "./SlotAvailabilityIndicator";
import { FieldLabel }                             from "./FieldLabel";
import { TattooIntakeFields } from "./TattooIntakeFields";
import { validateTattooIntake, type TattooIntakeValues } from "./tattooIntakeValidation";
import { CategorizedImagesField } from "./CategorizedImagesField";
import { DesiredPlacementField }                  from "./DesiredPlacementField";
import { AppointmentAttachmentCategory } from "../appointment.types";
import type { AppointmentResponse } from "../appointment.types";
import type { ArtistResponse }      from "@/features/artists/artistsApi";
import type { DepositRuleResponse } from "@/features/deposit-rules/depositRule.types";

// ── Constants ────────────────────────────────────────────────────────────────

const VALID_DURATIONS = [30, 45, 60, 90, 120, 180, 240, 300, 360, 480] as const;

// Mirrors CreateAppointmentValidator.cs's MaxImageUrls.
const MAX_REFERENCE_IMAGES = 6;

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

// ── Schema ───────────────────────────────────────────────────────────────────

const schema = z.object({
  artistId:        z.string().nullable(),
  bookAnyArtist:   z.boolean(),
  clientId:        z.string().min(1, "Select a client"),
  scheduledAt:     z.string().min(1, "Select date and time").refine(
    (v) => new Date(v) > new Date(),
    "Appointment must be in the future"
  ),
  durationMinutes: z.number().refine(
    (v) => (VALID_DURATIONS as readonly number[]).includes(v),
    "Select a valid appointment duration"
  ),
  depositRuleId:   z.string().nullable().optional(),
  notes:           z.string().optional(),
}).refine(
  (data) => data.bookAnyArtist || (!!data.artistId && data.artistId.length > 0),
  { message: "Select an artist", path: ["artistId"] },
);

type FormValues = z.infer<typeof schema>;

// ── Sub-components ────────────────────────────────────────────────────────────

function ArtistAvatar({ artist }: { artist: ArtistResponse }) {
  const initials = `${artist.firstName[0] ?? ""}${artist.lastName[0] ?? ""}`.toUpperCase();
  const [imageFailed, setImageFailed] = useState(false);

  if (artist.avatarUrl && !imageFailed) {
    return (
      <img
        src={artist.avatarUrl}
        alt=""
        aria-hidden="true"
        className="h-6 w-6 rounded-full object-cover shrink-0"
        onError={() => setImageFailed(true)}
      />
    );
  }

  return (
    <span
      aria-hidden="true"
      className="h-6 w-6 rounded-full bg-violet-600/20 text-violet-700 dark:text-violet-400
                 text-[9px] font-semibold flex items-center justify-center shrink-0"
    >
      {initials}
    </span>
  );
}

// Use the Radix primitive directly so ItemText wraps only the name (not avatar initials/spec)
function ArtistSelectItem({ artist }: { artist: ArtistResponse }) {
  return (
    <SelectPrimitive.Item
      value={artist.id}
      className="relative flex w-full cursor-default select-none items-center rounded-sm
                 py-1.5 pl-8 pr-2 text-sm outline-none
                 focus:bg-accent focus:text-accent-foreground
                 data-[disabled]:pointer-events-none data-[disabled]:opacity-50"
    >
      <span className="absolute left-2 flex h-3.5 w-3.5 items-center justify-center">
        <SelectPrimitive.ItemIndicator>
          <Check className="h-4 w-4" />
        </SelectPrimitive.ItemIndicator>
      </span>
      <span className="flex items-center gap-2">
        <ArtistAvatar artist={artist} />
        <span className="flex flex-col">
          <SelectPrimitive.ItemText>
            {artist.firstName} {artist.lastName}
          </SelectPrimitive.ItemText>
          {artist.specializations && (
            <span aria-hidden="true" className="text-[10px] text-muted-foreground truncate max-w-[180px]">
              {artist.specializations}
            </span>
          )}
        </span>
      </span>
    </SelectPrimitive.Item>
  );
}

function DepositPreview({
  ruleId,
  durationMinutes,
  activeRules,
  hourlyRate,
}: {
  ruleId:          string;
  durationMinutes: number;
  activeRules:     DepositRuleResponse[];
  hourlyRate:      number | null;
}) {
  const rule = activeRules.find((r) => r.id === ruleId);
  if (!rule) return null;

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
      <span className="text-sm font-semibold tabular-nums">
        €{estimated.toFixed(2)}
      </span>
    </div>
  );
}

// ── Main component ────────────────────────────────────────────────────────────

export function BookAppointmentForm() {
  const user = useCurrentUser();
  const role = useAppSelector((s) => s.auth.role);

  const isClientRole = role === Role.Client;
  const isStaffRole  = role === Role.Artist || role === Role.Owner || role === Role.Issuer;
  const tenantId     = useAppSelector((s) => s.auth.tenantId);

  // A logged-in client can arrive here from a DIFFERENT studio's public page
  // (?studio=<slug>) than the one their session is currently scoped to — resolve
  // the slug and switch the active studio before fetching anything tenant-scoped,
  // otherwise the form silently books at the wrong studio (the bug this fixes).
  const [searchParams] = useSearchParams();
  const studioSlug = searchParams.get("studio");

  const {
    data:       targetStudio,
    isFetching: resolvingStudioSlug,
    isError:    studioLookupFailed,
  } = useGetPublicStudioQuery(studioSlug ?? "", { skip: !studioSlug });

  const { isSwitching, error: switchError, ensure } = useEnsureActiveStudio();
  const [switchAttempted, setSwitchAttempted] = useState(false);
  const [switchFailed,    setSwitchFailed]    = useState(false);

  useEffect(() => {
    if (!studioSlug || !targetStudio) return;
    let cancelled = false;
    ensure(targetStudio.studioId).then((success) => {
      if (cancelled) return;
      setSwitchFailed(!success);
      setSwitchAttempted(true);
    });
    return () => { cancelled = true; };
  }, [studioSlug, targetStudio, ensure]);

  // Blocks all tenant-scoped queries below until we're certain the session is
  // scoped to the right studio (or there was never a studio to switch to).
  const studioReady = !studioSlug || (switchAttempted && !switchFailed);

  const { data: artists,      isLoading: loadingArtists } = useGetArtistsQuery(undefined, {
    skip: !studioReady,
  });
  const { data: clients,      isLoading: loadingClients } = useGetClientsQuery(undefined, {
    skip: isClientRole || !studioReady,
  });
  const { data: myClient }     = useGetMyClientQuery(undefined, { skip: !isClientRole || !studioReady });
  const { data: depositRules } = useGetDepositRulesQuery(undefined, { skip: !studioReady });

  const [createAppointment, { isLoading }] = useCreateAppointmentMutation();

  const [booked,      setBooked]      = useState<AppointmentResponse | null>(null);
  const [depositDone, setDepositDone] = useState<"paid" | "cash" | "skipped" | null>(null);
  const [artistSearch, setArtistSearch] = useState("");

  // Area photo + reference images — uploaded to R2 as they're picked (same presign→PUT flow as
  // Design revisions), before the appointment itself exists, so objects live under a
  // per-form-session key rather than an appointment id. Both optional here (unlike the guest
  // form, which requires both — Decision #6/Part 6d note: not flipping this existing form's
  // established optional-images expectation without an explicit go-ahead).
  const [uploadSessionId] = useState(() => generateUuid());
  const { upload: uploadImage } = usePresignedUpload();

  function buildUpload(category: AppointmentAttachmentCategory) {
    return async (file: File) => {
      const ext = ACCEPTED_IMAGE_TYPES[file.type];
      const objectKey = `appointments/pending/${uploadSessionId}/${category}/${Date.now()}-${generateUuid()}.${ext}`;
      return uploadImage(file, objectKey);
    };
  }

  const areaPhotos = useCategorizedImageUpload({
    maxImages: MAX_REFERENCE_IMAGES,
    upload:    buildUpload(AppointmentAttachmentCategory.AreaPhoto),
  });
  const referenceImages = useCategorizedImageUpload({
    maxImages: MAX_REFERENCE_IMAGES,
    upload:    buildUpload(AppointmentAttachmentCategory.Reference),
  });

  const anyImageUploading = areaPhotos.uploading || referenceImages.uploading;

  // Booking-content intake fields — kept outside react-hook-form, same pattern the pre-existing
  // image state already used, since these were added on top of an already-shipped schema.
  const [intake, setIntake] = useState<TattooIntakeValues>({
    tattooDescription: "", referralSource: "", referralSourceOther: "", safetyNotes: "",
  });
  const [tattooDescriptionError, setTattooDescriptionError] = useState<string | null>(null);
  const [referralSourceOtherError, setReferralSourceOtherError] = useState<string | null>(null);
  const [desiredPlacement, setDesiredPlacement] = useState<string[]>([]);

  const {
    register,
    control,
    handleSubmit,
    setValue,
    formState: { errors },
    reset: resetForm,
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      artistId:        "",
      bookAnyArtist:   false,
      durationMinutes: 60,
      clientId:        isClientRole ? (user?.id ?? "") : "",
      depositRuleId:   null,
    },
  });

  const watchedArtistId      = useWatch({ control, name: "artistId" });
  const watchedBookAnyArtist = useWatch({ control, name: "bookAnyArtist" });
  const watchedDate          = useWatch({ control, name: "scheduledAt" });
  const watchedDuration      = useWatch({ control, name: "durationMinutes" });
  const watchedDepositRuleId = useWatch({ control, name: "depositRuleId" });

  // Keep clientId current for client role
  useEffect(() => {
    if (isClientRole && myClient?.id) {
      setValue("clientId", myClient.id);
    }
  }, [isClientRole, myClient?.id, setValue]);

  // Dedup artists by id (safety net against any backend duplicate)
  const uniqueArtists = useMemo(() => {
    const seen = new Set<string>();
    return (artists ?? []).filter((a) => {
      if (seen.has(a.id)) return false;
      seen.add(a.id);
      return true;
    });
  }, [artists]);

  const filteredArtists = useMemo(() => {
    const term = artistSearch.toLowerCase().trim();
    if (!term) return uniqueArtists;
    return uniqueArtists.filter((a) =>
      `${a.firstName} ${a.lastName}`.toLowerCase().includes(term) ||
      (a.specializations ?? "").toLowerCase().includes(term),
    );
  }, [uniqueArtists, artistSearch]);

  const selectedArtist = useMemo(
    () => uniqueArtists.find((a) => a.id === watchedArtistId) ?? null,
    [uniqueArtists, watchedArtistId],
  );

  const debouncedCheck = useDebouncedSlotCheckArgs(
    watchedArtistId, watchedBookAnyArtist, watchedDate, watchedDuration,
  );

  const {
    data:       slotStatus,
    isFetching: checkingSlot,
  } = useCheckSlotAvailabilityQuery(debouncedCheck!, {
    skip: debouncedCheck === null,
  });

  const activeRules = depositRules?.filter((r) => r.isActive) ?? [];

  async function onSubmit(values: FormValues) {
    const { tattooDescriptionError, referralSourceOtherError } = validateTattooIntake(intake);
    setTattooDescriptionError(tattooDescriptionError);
    setReferralSourceOtherError(referralSourceOtherError);
    if (tattooDescriptionError || referralSourceOtherError) return;

    const clientId = isClientRole ? (myClient?.id ?? values.clientId) : values.clientId;
    const images = [
      ...areaPhotos.doneUrls().map((url) => ({ url, category: AppointmentAttachmentCategory.AreaPhoto })),
      ...referenceImages.doneUrls().map((url) => ({ url, category: AppointmentAttachmentCategory.Reference })),
    ];
    const result = await createAppointment({
      artistId:        values.bookAnyArtist ? null : values.artistId,
      clientId,
      date:            new Date(values.scheduledAt).toISOString(),
      durationMinutes: values.durationMinutes,
      // NOTE: depositRuleId is sent but ignored by the backend, which always auto-selects the
      // single active DepositRule if any — a pre-existing mismatch, not fixed in this pass
      // (see docs/claude/overnight-prompt-guest-checkout-booking-2026-08-31.md Part 6d).
      depositRuleId:   values.depositRuleId ?? null,
      notes:           values.notes || null,
      tattooDescription:          intake.tattooDescription,
      safetyNotes:                intake.safetyNotes || null,
      desiredPlacementLocations:  desiredPlacement,
      referralSource:             intake.referralSource || null,
      referralSourceOther:        intake.referralSourceOther || null,
      ...(images.length > 0 ? { images } : {}),
    });
    if ("data" in result) {
      toast.success("Appointment requested.");
      setBooked(result.data ?? null);
      resetForm({
        artistId:        "",
        bookAnyArtist:   false,
        durationMinutes: 60,
        clientId:        isClientRole ? (myClient?.id ?? user?.id ?? "") : "",
        depositRuleId:   null,
      });
      setArtistSearch("");
      // No explicit debouncedCheck reset needed — useDebouncedSlotCheckArgs derives it from the
      // same watched fields resetForm() above already clears, so it naturally settles to null.
      areaPhotos.clear();
      referenceImages.clear();
      setIntake({ tattooDescription: "", referralSource: "", referralSourceOther: "", safetyNotes: "" });
      setDesiredPlacement([]);
    } else {
      const errMsg =
        (result.error as { data?: { message?: string } } | undefined)?.data?.message
        ?? "Failed to book appointment.";
      toast.error(errMsg);
    }
  }

  function startOver() {
    setBooked(null);
    setDepositDone(null);
  }

  // Step 0 — resolving/switching to the studio linked from ?studio=<slug>
  if (studioSlug && studioLookupFailed) {
    return (
      <div
        role="alert"
        className="flex items-center gap-2 rounded-md border border-destructive/30
                   bg-destructive/5 px-3 py-3 text-sm text-destructive-text"
      >
        <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
        This studio couldn&apos;t be found.
      </div>
    );
  }

  if (studioSlug && switchFailed) {
    return (
      <div
        role="alert"
        className="flex items-center gap-2 rounded-md border border-destructive/30
                   bg-destructive/5 px-3 py-3 text-sm text-destructive-text"
      >
        <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
        {switchError ?? "Couldn't switch studios. Please try again."}
      </div>
    );
  }

  if (studioSlug && (resolvingStudioSlug || isSwitching || !switchAttempted)) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 py-10
                       text-sm text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" aria-hidden="true" />
        Switching to this studio…
      </div>
    );
  }

  // A studio-less client (signed up with no studio, or hasn't booked anywhere yet)
  // landing here directly has no active tenant and nothing bookable — send them to
  // Discover instead of showing a broken, empty artist dropdown.
  if (isClientRole && !studioSlug && !tenantId) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 py-10 text-center">
        <p className="text-sm text-muted-foreground">
          You haven&apos;t joined a studio yet. Browse studios to book your first appointment.
        </p>
        <Button asChild className="bg-violet-600 hover:bg-violet-700 text-white">
          <Link to="/discover">Browse studios</Link>
        </Button>
      </div>
    );
  }

  // Step 2 — deposit (clients only, when the appointment requires one)
  if (booked && isClientRole && booked.depositAmount > 0 && !depositDone) {
    return (
      <div className="space-y-4">
        <div className="text-center space-y-1">
          <p className="text-sm font-medium">Appointment requested!</p>
          <p className="text-xs text-muted-foreground">
            Secure your slot with a deposit of{" "}
            <span className="font-medium text-foreground">€{booked.depositAmount.toFixed(2)}</span>.
          </p>
        </div>

        <PaymentMethodSelector
          appointmentId={booked.id}
          amount={booked.depositAmount}
          onSuccess={(method) => setDepositDone(method === "cash" ? "cash" : "paid")}
          onError={(message) => toast.error(message)}
        />

        <button
          type="button"
          onClick={() => setDepositDone("skipped")}
          className="w-full text-xs text-muted-foreground underline underline-offset-4 hover:text-foreground"
        >
          I&apos;ll sort the deposit out later
        </button>
      </div>
    );
  }

  // Step 3 — confirmation
  if (booked) {
    return (
      <div className="text-center space-y-3 py-6">
        {depositDone === "cash" ? (
          <Banknote className="h-8 w-8 mx-auto text-green-500" />
        ) : (
          <CheckCircle2 className="h-8 w-8 mx-auto text-green-500" />
        )}
        <p className="text-sm font-medium">Appointment requested!</p>
        <p className="text-xs text-muted-foreground">
          {depositDone === "paid"
            ? booked.artistId
              ? "Your deposit is authorised — the artist will confirm soon."
              : "Your deposit is authorised — the studio will assign an artist and confirm soon."
            : depositDone === "cash"
            ? booked.artistId
              ? "Bring the deposit in cash to the studio. The artist will confirm soon."
              : "Bring the deposit in cash to the studio. The studio will assign an artist and confirm soon."
            : depositDone === "skipped"
            ? booked.artistId
              ? "The studio will contact you about the deposit. The artist will confirm soon."
              : "The studio will contact you about the deposit and assign an artist soon."
            : booked.artistId
            ? "The artist will confirm soon."
            : "The studio will assign an artist and confirm soon."}
        </p>
        <Button variant="outline" size="sm" onClick={startOver}>
          Book another
        </Button>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
      {/* text-muted-foreground/75 ≈ 4.7:1 on the dark theme's #09090b background — passes WCAG
          AA (measured 2026-09-05 while adding axe-core e2e coverage; /60 measured 3.38:1). */}
      <p className="text-xs text-muted-foreground">* Required</p>

      {/* Let the studio choose */}
      <div className="flex items-center justify-between rounded-md border border-border/40
                      bg-muted/20 px-3 py-2">
        <div>
          <p className="text-xs font-medium">Let the studio choose my artist</p>
          <p className="text-[11px] text-muted-foreground">
            We&apos;ll confirm someone&apos;s available — the studio assigns your artist before
            confirming.
          </p>
        </div>
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

      {/* Artist selector */}
      {!watchedBookAnyArtist && (
        <div className="space-y-1.5">
          <FieldLabel htmlFor="artistId" required>Artist</FieldLabel>
          <Controller
            control={control}
            name="artistId"
            render={({ field }) => (
              <Select
                disabled={loadingArtists}
                value={field.value ?? ""}
                onValueChange={field.onChange}
              >
                <SelectTrigger
                  id="artistId"
                  aria-label="Select artist"
                  className={cn(errors.artistId && "border-destructive")}
                >
                  {field.value && selectedArtist ? (
                    <span className="flex items-center gap-2">
                      <ArtistAvatar artist={selectedArtist} />
                      <span>{selectedArtist.firstName} {selectedArtist.lastName}</span>
                    </span>
                  ) : (
                    <SelectValue placeholder={loadingArtists ? "Loading artists…" : "Choose an artist"} />
                  )}
                </SelectTrigger>
                <SelectContent>
                  <div className="px-2 pb-1.5 pt-1">
                    <input
                      type="text"
                      placeholder="Search artists…"
                      value={artistSearch}
                      onChange={(e) => setArtistSearch(e.target.value)}
                      className="w-full rounded-sm border-0 bg-muted/50 px-2 py-1
                                 text-xs placeholder:text-muted-foreground
                                 focus:outline-none focus:ring-1 focus:ring-ring"
                      aria-label="Search artists"
                    />
                  </div>
                  {filteredArtists.length === 0 ? (
                    <div className="py-4 text-center text-xs text-muted-foreground">
                      {artists?.length === 0
                        ? "No artists configured for this studio."
                        : "No artists match your search."}
                    </div>
                  ) : (
                    filteredArtists.map((a) => (
                      <ArtistSelectItem key={a.id} artist={a} />
                    ))
                  )}
                </SelectContent>
              </Select>
            )}
          />
          {errors.artistId && (
            <p className="text-xs text-destructive-text" role="alert">
              {errors.artistId.message}
            </p>
          )}
        </div>
      )}

      {/* Client selector — visible for staff roles only */}
      {isStaffRole && (
        <div className="space-y-1.5">
          <FieldLabel htmlFor="clientId" required>Client</FieldLabel>
          <Controller
            control={control}
            name="clientId"
            render={({ field }) => (
              <Select
                disabled={loadingClients}
                value={field.value}
                onValueChange={field.onChange}
              >
                <SelectTrigger id="clientId" className={cn(errors.clientId && "border-destructive")}>
                  <SelectValue placeholder={loadingClients ? "Loading…" : "Select a client"} />
                </SelectTrigger>
                <SelectContent>
                  {clients?.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.firstName} {c.lastName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
          {errors.clientId && (
            <p className="text-xs text-destructive-text" role="alert">
              {errors.clientId.message}
            </p>
          )}
        </div>
      )}

      {/* Date & Time + Appointment Duration */}
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
            <p className="text-xs text-destructive-text" role="alert">
              {errors.scheduledAt.message}
            </p>
          )}
        </div>

        <div className="space-y-1.5 col-span-2 sm:col-span-1">
          <FieldLabel htmlFor="durationMinutes" required>Appointment Duration</FieldLabel>
          <Controller
            control={control}
            name="durationMinutes"
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v))}
              >
                <SelectTrigger
                  id="durationMinutes"
                  className={cn(errors.durationMinutes && "border-destructive")}
                >
                  <SelectValue placeholder="Select duration" />
                </SelectTrigger>
                <SelectContent>
                  {DURATION_OPTIONS.map(({ value, label }) => (
                    <SelectItem key={value} value={String(value)}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
          {errors.durationMinutes && (
            <p className="text-xs text-destructive-text" role="alert">
              {errors.durationMinutes.message}
            </p>
          )}
        </div>
      </div>

      {/* Slot availability indicator */}
      {debouncedCheck !== null && (
        <SlotAvailabilityIndicator checking={checkingSlot} status={slotStatus} />
      )}

      {/* Deposit rule — shown when the studio has at least one active rule */}
      {activeRules.length > 0 && (
        <div className="space-y-1.5">
          <FieldLabel htmlFor="depositRuleId">Deposit rule</FieldLabel>
          <Controller
            control={control}
            name="depositRuleId"
            render={({ field }) => (
              <Select
                value={field.value ?? "none"}
                onValueChange={(v) => field.onChange(v === "none" ? null : v)}
              >
                <SelectTrigger id="depositRuleId">
                  <SelectValue placeholder="No deposit" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">No deposit</SelectItem>
                  {activeRules.map((rule) => (
                    <SelectItem key={rule.id} value={rule.id}>
                      {rule.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />

          {watchedDepositRuleId && watchedDepositRuleId !== "none" && watchedDuration > 0 && (
            <DepositPreview
              ruleId={watchedDepositRuleId}
              durationMinutes={watchedDuration}
              activeRules={activeRules}
              hourlyRate={selectedArtist?.hourlyRate ?? null}
            />
          )}
        </div>
      )}

      {/* Tattoo description, referral source, safety notes — shared with guest checkout */}
      <TattooIntakeFields
        value={intake}
        onChange={setIntake}
        tattooDescriptionError={tattooDescriptionError ?? undefined}
        referralSourceOtherError={referralSourceOtherError ?? undefined}
      />

      {/* Desired placement */}
      <DesiredPlacementField locations={desiredPlacement} onChange={setDesiredPlacement} />

      {/* Notes */}
      <div className="space-y-1.5">
        <FieldLabel htmlFor="notes">Notes</FieldLabel>
        <Textarea
          id="notes"
          rows={2}
          placeholder="Anything else for the studio?"
          {...register("notes")}
          className="resize-none"
        />
      </div>

      {/* Area photo + reference images */}
      <CategorizedImagesField
        category={AppointmentAttachmentCategory.AreaPhoto}
        label="Area photo"
        helperText={`Click to add a photo of the area — JPEG, PNG, or WebP (up to ${MAX_REFERENCE_IMAGES})`}
        required={false}
        max={MAX_REFERENCE_IMAGES}
        images={areaPhotos.images}
        error={areaPhotos.error}
        onPick={(files) => void areaPhotos.pick(files)}
        onRemove={areaPhotos.remove}
        disabled={isLoading}
      />
      <CategorizedImagesField
        category={AppointmentAttachmentCategory.Reference}
        label="Reference images"
        helperText={`Click to add photos — JPEG, PNG, or WebP (up to ${MAX_REFERENCE_IMAGES})`}
        required={false}
        max={MAX_REFERENCE_IMAGES}
        images={referenceImages.images}
        error={referenceImages.error}
        onPick={(files) => void referenceImages.pick(files)}
        onRemove={referenceImages.remove}
        disabled={isLoading}
      />

      <Button
        type="submit"
        className="w-full bg-violet-600 hover:bg-violet-700 text-white font-medium
                   disabled:bg-violet-600/50"
        disabled={isLoading || slotStatus?.available === false || anyImageUploading}
      >
        {isLoading ? (
          <><Loader2 className="h-4 w-4 animate-spin mr-2" aria-hidden="true" />Booking…</>
        ) : anyImageUploading ? (
          <><Loader2 className="h-4 w-4 animate-spin mr-2" aria-hidden="true" />Uploading images…</>
        ) : (
          "Request Appointment"
        )}
      </Button>

      {/* text-muted-foreground/75 — see the "* Required" note above for the measured ratio. */}
      <p className="text-center text-[11px] text-muted-foreground">
        Your artist will confirm availability within 24 hours.
      </p>
    </form>
  );
}
