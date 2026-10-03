import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, PenLine } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { getRoleRedirectPath } from "@/app/router";
import {
  useLoginMutation,
  useOauthLoginMutation,
  useOauthRegisterMutation,
  useRegisterUserMutation,
  useRegisterSoloArtistMutation,
} from "@/features/auth/authApi";
import { setCredentials, setPendingReferralCode } from "@/features/auth/authSlice";
import { OAuthButtons } from "@/shared/components/OAuthButtons";
import { GuestAuthHeader } from "@/shared/components/GuestAuthHeader";
import { Button } from "@/shared/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { LocationPicker } from "@/shared/components/ui/location-picker";
import { PasswordInput } from "@/shared/components/ui/password-input";
import { PasswordStrengthMeter } from "@/shared/components/ui/PasswordStrengthMeter";
import { CurrencySelect } from "@/shared/components/ui/currency-select";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/shared/components/ui/select";
import { useAddressGeocode } from "@/shared/hooks/useAddressGeocode";
import { PHONE_COUNTRIES, flagEmoji } from "@/shared/utils/phoneCountries";
import { decodeToken } from "@/shared/utils/jwt";
import { useRegisterStudioMutation } from "../studiosApi";
import { useGetCountryDefaultCurrencyQuery, useGetVisitorGeoQuery } from "@/features/public/publicApi";
import { registrationTimezoneId } from "@/shared/utils/timezones";

const schema = z
  .object({
    name: z.string().min(1, "Studio name is required").max(200),
    slug: z
      .string()
      .min(1, "Slug is required")
      .max(100)
      .regex(
        /^[a-z0-9-]+$/,
        "Slug may only contain lowercase letters, numbers, and hyphens."
      ),
    city: z.string().min(1, "City is required").max(100),
    nipt: z
      .string()
      .trim()
      .length(10, "NIPT must be exactly 10 characters")
      .regex(
        /^[A-Za-z]\d{8}[A-Za-z]$/,
        "NIPT format looks wrong — expected a letter, 8 digits, then a letter (e.g. L01234567A)"
      )
      .transform((v) => v.toUpperCase()),
    addressLine1: z.string().min(1, "Street address is required").max(300),
    addressLine2: z.string().max(150).optional(),
    postalCode: z.string().max(20).optional(),
    countryCode: z.string().length(2, "Country is required"),
    currency: z.string().min(1, "Please choose your studio's currency."),
    latitude: z
      .number({ error: "Latitude is required" })
      .min(-90, "Must be between -90 and 90")
      .max(90, "Must be between -90 and 90"),
    longitude: z
      .number({ error: "Longitude is required" })
      .min(-180, "Must be between -180 and 180")
      .max(180, "Must be between -180 and 180"),
    email: z
      .string()
      .min(1, "Email is required")
      .max(256)
      .email("Enter a valid email"),
    password: z.string(),
    confirmPassword: z.string(),
  })
  .superRefine((data, ctx) => {
    // Both fields empty means the user is on the OAuth path — skip password validation.
    if (data.password === "" && data.confirmPassword === "") return;

    if (data.password.length < 8) {
      ctx.addIssue({
        code: "custom",
        message: "Password must be at least 8 characters",
        path: ["password"],
      });
    }

    if (data.password !== data.confirmPassword) {
      ctx.addIssue({
        code: "custom",
        message: "Passwords do not match",
        path: ["confirmPassword"],
      });
    }
  });

type FormValues = z.infer<typeof schema>;

const STEP_1_FIELDS = [
  "name", "slug", "city", "nipt", "addressLine1", "latitude", "longitude", "countryCode", "currency",
] as const;

const soloSchema = z.object({
  firstName: z.string().min(1, "First name is required").max(100),
  lastName:  z.string().min(1, "Last name is required").max(100),
  email:     z.string().min(1, "Email is required").max(256).email("Enter a valid email"),
  password:  z.string().min(8, "Password must be at least 8 characters"),
  confirmPassword: z.string(),
  countryCode: z.string().length(2),
  currency: z.string().min(1),
}).superRefine((data, ctx) => {
  if (data.password !== data.confirmPassword) {
    ctx.addIssue({
      code: "custom",
      message: "Passwords do not match",
      path: ["confirmPassword"],
    });
  }
});

type SoloFormValues = z.infer<typeof soloSchema>;

export function RegisterStudioPage() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const existingRole = useAppSelector((s) => s.auth.role);
  const pendingReferralCode = useAppSelector((s) => s.auth.pendingReferralCode);
  const [searchParams] = useSearchParams();

  const [step, setStep] = useState<1 | 2>(1);
  const [serverError, setServerError] = useState<string | null>(null);
  const slugManuallyEdited = useRef(false);
  const [oauthProvider, setOauthProvider] = useState<"google" | "apple" | null>(null);
  const [oauthIdToken, setOauthIdToken] = useState<string | null>(null);
  const [mode, setMode] = useState<"studio" | "solo">("studio");
  const [soloServerError, setSoloServerError] = useState<string | null>(null);

  const [registerStudio] = useRegisterStudioMutation();
  const [registerUser] = useRegisterUserMutation();
  const [registerSoloArtist, { isLoading: isSoloSubmitting }] = useRegisterSoloArtistMutation();
  const [login] = useLoginMutation();
  const [oauthRegister] = useOauthRegisterMutation();
  const [oauthLogin] = useOauthLoginMutation();

  // The country defaults, in order, to: where the visitor's IP is (server GeoIP lookup, applied
  // below when it arrives), a best-effort browser-language guess, then Albania. There is no address
  // to geocode on this short-signup path, unlike the studio path's typed address. The owner can
  // always correct it via "Change currency" below, or later in Studio Settings.
  const { data: visitorGeo } = useGetVisitorGeoQuery();
  const browserRegionGuess = (() => {
    try {
      return new Intl.Locale(navigator.language).maximize().region ?? "AL";
    } catch {
      return "AL";
    }
  })();

  const [soloCurrencyExpanded, setSoloCurrencyExpanded] = useState(false);
  const soloCurrencyManuallyEdited = useRef(false);
  const soloCountryManuallyEdited = useRef(false);

  const {
    register: registerSolo,
    handleSubmit: handleSoloSubmit,
    watch: watchSolo,
    setValue: setSoloValue,
    formState: { errors: soloErrors },
  } = useForm<SoloFormValues>({
    resolver: zodResolver(soloSchema),
    defaultValues: {
      firstName: "", lastName: "", email: "", password: "", confirmPassword: "",
      countryCode: browserRegionGuess, currency: "",
    },
  });

  const soloCountryCode = watchSolo("countryCode");
  const { data: soloCountryDefault, isFetching: soloCurrencyLoading } = useGetCountryDefaultCurrencyQuery(
    soloCountryCode,
    { skip: soloCountryCode.length !== 2 },
  );

  // The IP lookup is async: apply it once it arrives, never over a country the person picked.
  useEffect(() => {
    if (!soloCountryManuallyEdited.current && visitorGeo?.countryCode) {
      setSoloValue("countryCode", visitorGeo.countryCode);
    }
  }, [visitorGeo, setSoloValue]);

  useEffect(() => {
    if (!soloCurrencyManuallyEdited.current && soloCountryDefault?.currency) {
      setSoloValue("currency", soloCountryDefault.currency);
    }
  }, [soloCountryDefault, setSoloValue]);

  async function onSoloSubmit(values: SoloFormValues) {
    setSoloServerError(null);
    try {
      await registerSoloArtist({
        firstName: values.firstName,
        lastName:  values.lastName,
        email:     values.email,
        password:  values.password,
        countryCode: values.countryCode,
        currency:    values.currency || undefined,
        timezone:    registrationTimezoneId(values.countryCode, visitorGeo?.countryCode, visitorGeo?.timeZone),
      }).unwrap();

      const { accessToken, refreshToken } = await login({
        email: values.email,
        password: values.password,
      }).unwrap();

      dispatch(setCredentials({ ...decodeToken(accessToken), refreshToken }));
      navigate("/dashboard", { replace: true });
    } catch (err) {
      const message =
        typeof err === "object" && err !== null && "data" in err
          ? ((err as { data: { message?: string; detail?: string } }).data?.message ??
            (err as { data: { message?: string; detail?: string } }).data?.detail ??
            "Registration failed. Please try again.")
          : "Unable to reach the server. Please try again.";
      setSoloServerError(message);
    }
  }

  const {
    register,
    handleSubmit,
    watch,
    setValue,
    trigger,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      slug: "",
      city: "",
      nipt: "",
      addressLine1: "",
      addressLine2: "",
      postalCode: "",
      countryCode: "AL",
      currency: "",
      latitude: NaN,
      longitude: NaN,
      email: "",
      password: "",
      confirmPassword: "",
    },
  });

  const nameValue = watch("name");
  const slugValue = watch("slug");
  const latValue  = watch("latitude");
  const lngValue  = watch("longitude");
  const cityValue = watch("city");
  const addressLine1Value = watch("addressLine1");
  const countryCodeValue = watch("countryCode");
  const currencyValue = watch("currency");

  const countryManuallyEdited = useRef(false);
  const currencyManuallyEdited = useRef(false);

  const { data: countryDefaultCurrency, isFetching: currencyLoading } = useGetCountryDefaultCurrencyQuery(
    countryCodeValue,
    { skip: countryCodeValue.length !== 2 },
  );

  useEffect(() => {
    if (!currencyManuallyEdited.current && countryDefaultCurrency?.currency) {
      setValue("currency", countryDefaultCurrency.currency, { shouldValidate: true });
    }
    // A country whose default currency couldn't be resolved (unknown to RegionInfo) leaves the
    // field empty and required — never silently falls back to a guessed currency.
  }, [countryDefaultCurrency, setValue]);

  // Studio path: the country starts as Albania, moves to where the visitor's IP is when that
  // arrives, and then follows the typed or pinned address (below). It never overrides a country
  // the person picked or one an address already set.
  useEffect(() => {
    if (!countryManuallyEdited.current && !addressLine1Value && visitorGeo?.countryCode) {
      setValue("countryCode", visitorGeo.countryCode, { shouldValidate: true });
    }
    // addressLine1Value is read only to skip the default once an address exists; the effect must
    // re-run when the lookup arrives, not on every keystroke.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visitorGeo, setValue]);

  // Set right before a pin-driven setValue("addressLine1", ...) below, so the very next
  // render's useAddressGeocode call sees it and skips the forward-geocode fetch that
  // address change would otherwise trigger — pin drag already has authoritative lat/lng,
  // re-geocoding its own reverse-geocoded text back to coordinates is redundant and would
  // flash "Locating on the map…" right after the user just finished correcting the pin.
  const pinDrivenAddressUpdate = useRef(false);

  const { status: geocodeStatus } = useAddressGeocode(
    addressLine1Value,
    ({ lat, lng, city, countryCode }) => {
      setValue("latitude", lat, { shouldValidate: true });
      setValue("longitude", lng, { shouldValidate: true });
      setValue("city", city, { shouldValidate: true });
      if (!countryManuallyEdited.current && countryCode) {
        setValue("countryCode", countryCode, { shouldValidate: true });
      }
    },
    { enabled: !pinDrivenAddressUpdate.current }
  );

  useEffect(() => {
    pinDrivenAddressUpdate.current = false;
  }, [addressLine1Value]);

  useEffect(() => {
    if (existingRole) {
      navigate(getRoleRedirectPath(existingRole), { replace: true });
    }
  }, [existingRole, navigate]);

  useEffect(() => {
    const ref = searchParams.get("ref");
    if (ref) dispatch(setPendingReferralCode(ref));
  }, [searchParams, dispatch]);

  useEffect(() => {
    if (!slugManuallyEdited.current) {
      const auto = nameValue
        .toLowerCase()
        .replace(/\s+/g, "-")
        .replace(/[^a-z0-9-]/g, "")
        .replace(/-+/g, "-")
        .replace(/^-|-$/g, "");
      setValue("slug", auto);
    }
  }, [nameValue, setValue]);

  async function handleNext() {
    const valid = await trigger([...STEP_1_FIELDS]);
    if (valid) {
      setServerError(null);
      setStep(2);
    }
  }

  async function handleOAuthToken({
    provider,
    idToken,
  }: {
    provider: "google" | "apple";
    idToken: string;
  }) {
    // Decode the provider ID token to pre-fill the email field for display purposes
    // only — the backend re-validates the signature and extracts the trusted email itself.
    try {
      const parts = idToken.split(".");
      if (parts.length !== 3) throw new Error("Malformed token");
      const claims = JSON.parse(atob(parts[1].replace(/-/g, "+").replace(/_/g, "/")));
      const email = (claims.email as string | undefined) ?? "";

      setValue("email", email);
      setValue("password", "");
      setValue("confirmPassword", "");
    } catch {
      // If we can't decode the token client-side, we still proceed — the backend
      // extracts the email from the validated token.
    }

    setOauthProvider(provider);
    setOauthIdToken(idToken);
  }

  async function onSubmit(values: FormValues) {
    setServerError(null);
    try {
      const studio = await registerStudio({
        name:         values.name,
        slug:         values.slug,
        city:         values.city,
        nipt:         values.nipt,
        addressLine1: values.addressLine1,
        addressLine2: values.addressLine2 || undefined,
        postalCode:   values.postalCode || undefined,
        latitude:     values.latitude,
        longitude:    values.longitude,
        ownerEmail:   values.email,
        countryCode:  values.countryCode,
        currency:     values.currency,
        timezone:     registrationTimezoneId(values.countryCode, visitorGeo?.countryCode, visitorGeo?.timeZone),
        ...(pendingReferralCode ? { referralCode: pendingReferralCode } : {}),
      }).unwrap();

      if (oauthProvider && oauthIdToken) {
        await oauthRegister({
          provider: oauthProvider,
          idToken:  oauthIdToken,
          role:     "owner",
          studioId: studio.id,
        }).unwrap();

        const { accessToken, refreshToken } = await oauthLogin({
          provider: oauthProvider,
          idToken:  oauthIdToken,
        }).unwrap();

        dispatch(setCredentials({ ...decodeToken(accessToken), refreshToken }));
      } else {
        await registerUser({
          email: values.email,
          password: values.password,
          role: "owner",
          studioId: studio.id,
        }).unwrap();

        const { accessToken, refreshToken } = await login({
          email: values.email,
          password: values.password,
        }).unwrap();

        dispatch(setCredentials({ ...decodeToken(accessToken), refreshToken }));
      }

      dispatch(setPendingReferralCode(null));
      navigate("/dashboard", { replace: true });
    } catch (err) {
      const message =
        typeof err === "object" && err !== null && "data" in err
          ? ((err as { data: { message?: string; detail?: string } }).data?.message ??
            (err as { data: { message?: string; detail?: string } }).data?.detail ??
            "Registration failed. Please try again.")
          : "Unable to reach the server. Please try again.";
      setServerError(message);
    }
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <GuestAuthHeader />

      <div className="flex-1 flex items-center justify-center p-4">
        <div className="w-full max-w-md space-y-6">
          <div className="flex flex-col items-center gap-2 text-center">
            <div className="flex items-center gap-2">
              <PenLine className="h-8 w-8" />
              <span className="text-2xl font-semibold tracking-tight">TattooOS</span>
            </div>
            <p className="text-sm text-muted-foreground">Tattoo Studio Management</p>
          </div>

          <Card>
            <CardHeader>
              <CardTitle>
                {mode === "studio" ? "Register your studio" : "Register as an independent artist"}
              </CardTitle>
              <CardDescription>
                {mode === "studio"
                  ? `Step ${step} of 2 — ${step === 1 ? "Studio details" : "Owner account"}`
                  : "Just the basics — add your studio details later, once you're ready"}
              </CardDescription>
            </CardHeader>
            <CardContent>
              {!(mode === "studio" && step === 2) && (
                <div
                  role="group"
                  aria-label="Registration type"
                  className="mb-4 grid grid-cols-2 gap-1 rounded-md border p-0.5 text-sm"
                >
                  <button
                    type="button"
                    aria-pressed={mode === "studio"}
                    onClick={() => setMode("studio")}
                    className={`rounded px-2 py-1.5 font-medium transition-colors ${
                      mode === "studio"
                        ? "bg-foreground text-background"
                        : "text-muted-foreground hover:text-foreground"
                    }`}
                  >
                    I run a studio
                  </button>
                  <button
                    type="button"
                    aria-pressed={mode === "solo"}
                    onClick={() => setMode("solo")}
                    className={`rounded px-2 py-1.5 font-medium transition-colors ${
                      mode === "solo"
                        ? "bg-foreground text-background"
                        : "text-muted-foreground hover:text-foreground"
                    }`}
                  >
                    I'm an independent artist
                  </button>
                </div>
              )}

              {mode === "solo" && (
                <form onSubmit={handleSoloSubmit(onSoloSubmit)} noValidate className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <div className="space-y-1.5">
                      <Label htmlFor="solo-firstName">First name</Label>
                      <Input
                        id="solo-firstName"
                        {...registerSolo("firstName")}
                        aria-invalid={!!soloErrors.firstName}
                      />
                      {soloErrors.firstName && (
                        <p className="text-xs text-destructive-text">{soloErrors.firstName.message}</p>
                      )}
                    </div>
                    <div className="space-y-1.5">
                      <Label htmlFor="solo-lastName">Last name</Label>
                      <Input
                        id="solo-lastName"
                        {...registerSolo("lastName")}
                        aria-invalid={!!soloErrors.lastName}
                      />
                      {soloErrors.lastName && (
                        <p className="text-xs text-destructive-text">{soloErrors.lastName.message}</p>
                      )}
                    </div>
                  </div>

                  <div className="space-y-1.5">
                    <Label htmlFor="solo-email">Email</Label>
                    <Input
                      id="solo-email"
                      type="email"
                      autoComplete="email"
                      placeholder="you@example.com"
                      {...registerSolo("email")}
                      aria-invalid={!!soloErrors.email}
                    />
                    {soloErrors.email && (
                      <p className="text-xs text-destructive-text">{soloErrors.email.message}</p>
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <Label htmlFor="solo-password">Password</Label>
                    <PasswordInput
                      id="solo-password"
                      autoComplete="new-password"
                      {...registerSolo("password")}
                      aria-invalid={!!soloErrors.password}
                    />
                    {soloErrors.password && (
                      <p className="text-xs text-destructive-text">{soloErrors.password.message}</p>
                    )}
                    {watchSolo("password") !== "" && (
                      <PasswordStrengthMeter password={watchSolo("password")} />
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <Label htmlFor="solo-confirmPassword">Confirm password</Label>
                    <PasswordInput
                      id="solo-confirmPassword"
                      autoComplete="new-password"
                      {...registerSolo("confirmPassword")}
                      aria-invalid={!!soloErrors.confirmPassword}
                    />
                    {soloErrors.confirmPassword && (
                      <p className="text-xs text-destructive-text">{soloErrors.confirmPassword.message}</p>
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <div className="flex items-center justify-between">
                      <p className="text-xs text-muted-foreground">
                        Prices and payments will be in{" "}
                        <strong>{watchSolo("currency") || "…"}</strong>
                        {soloCurrencyLoading && " (checking your country's currency…)"}
                      </p>
                      {!soloCurrencyExpanded && (
                        <button
                          type="button"
                          onClick={() => setSoloCurrencyExpanded(true)}
                          className="text-xs underline underline-offset-2 hover:text-foreground text-muted-foreground shrink-0"
                        >
                          Change currency
                        </button>
                      )}
                    </div>
                    {soloCurrencyExpanded && (
                      <div className="grid grid-cols-2 gap-3">
                        <div className="space-y-1.5">
                          <Label htmlFor="solo-country">Country</Label>
                          <Select
                            value={soloCountryCode}
                            onValueChange={(v) => {
                              soloCountryManuallyEdited.current = true;
                              setSoloValue("countryCode", v);
                            }}
                          >
                            <SelectTrigger id="solo-country">
                              <SelectValue />
                            </SelectTrigger>
                            <SelectContent className="max-h-72" showScrollbar>
                              {PHONE_COUNTRIES.map((c) => (
                                <SelectItem key={c.code} value={c.code}>
                                  {flagEmoji(c.code)} {c.name}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </div>
                        <div className="space-y-1.5">
                          <Label htmlFor="solo-currency">Currency</Label>
                          <CurrencySelect
                            id="solo-currency"
                            value={watchSolo("currency") || null}
                            countryDefault={soloCountryDefault?.currency ?? null}
                            onChange={(code) => {
                              // Radix's hidden native-<select> autofill shim can fire a spurious
                              // onValueChange("") the first time `value` goes from unset to a real
                              // code — there's no real "blank currency" option in this list, so an
                              // empty callback value is always that shim, never a genuine pick.
                              if (!code) return;
                              soloCurrencyManuallyEdited.current = true;
                              setSoloValue("currency", code);
                            }}
                          />
                        </div>
                      </div>
                    )}
                  </div>

                  <p className="text-xs text-muted-foreground">
                    You'll be able to take bookings right away. Add your studio's business
                    details, or make it visible on the map, any time from Settings.
                  </p>

                  {soloServerError && (
                    <p className="text-sm text-destructive-text" role="alert">
                      {soloServerError}
                    </p>
                  )}

                  <Button type="submit" className="w-full" disabled={isSoloSubmitting}>
                    {isSoloSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                    Create my account
                  </Button>

                  <p className="text-center text-xs text-muted-foreground">
                    By registering you agree to our{" "}
                    <Link to="/terms" className="underline underline-offset-2 hover:text-foreground">
                      Terms of Service
                    </Link>{" "}
                    and{" "}
                    <Link to="/privacy" className="underline underline-offset-2 hover:text-foreground">
                      Privacy Policy
                    </Link>
                    .
                  </p>
                </form>
              )}

              {mode === "studio" && (
              <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
                {step === 1 && (
                  <>
                    <div className="space-y-1.5">
                      <Label htmlFor="name">Studio name</Label>
                      <Input
                        id="name"
                        placeholder="Ink & Soul Studio"
                        {...register("name")}
                        aria-invalid={!!errors.name}
                      />
                      {errors.name && (
                        <p className="text-xs text-destructive-text">{errors.name.message}</p>
                      )}
                    </div>

                    <div className="space-y-1.5">
                      <Label htmlFor="slug">URL slug</Label>
                      <Input
                        id="slug"
                        placeholder="ink-and-soul-studio"
                        {...register("slug", {
                          onChange: () => {
                            slugManuallyEdited.current = true;
                          },
                        })}
                        aria-invalid={!!errors.slug}
                      />
                      <p className="text-xs text-muted-foreground">
                        tattooos.co/
                        <strong>{slugValue || "your-slug"}</strong>
                      </p>
                      {errors.slug && (
                        <p className="text-xs text-destructive-text">{errors.slug.message}</p>
                      )}
                    </div>

                    <div className="space-y-1.5">
                      <Label htmlFor="nipt">Business tax ID (NIPT)</Label>
                      <Input
                        id="nipt"
                        placeholder="L01234567A"
                        {...register("nipt")}
                        aria-invalid={!!errors.nipt}
                        aria-describedby="nipt-help"
                      />
                      <p id="nipt-help" className="text-xs text-muted-foreground">
                        Your studio&apos;s NIPT, used for invoicing and business verification.
                        Format: one letter, 8 digits, one letter.
                      </p>
                      {errors.nipt && (
                        <p className="text-xs text-destructive-text">{errors.nipt.message}</p>
                      )}
                    </div>

                    <div className="space-y-1.5">
                      <Label htmlFor="addressLine1">Street address</Label>
                      <Input
                        id="addressLine1"
                        placeholder="Rruga e Kavajës 10"
                        {...register("addressLine1")}
                        aria-invalid={!!errors.addressLine1}
                        aria-describedby="addressLine1-help"
                      />
                      <p id="addressLine1-help" className="text-xs text-muted-foreground flex items-center gap-1">
                        {geocodeStatus === "loading" && (
                          <>
                            <Loader2 className="h-3 w-3 animate-spin" />
                            Locating on the map…
                          </>
                        )}
                        {geocodeStatus === "error" &&
                          "Couldn't find that address automatically — you can also click the map below to set your studio's location."}
                        {(geocodeStatus === "idle" || geocodeStatus === "success") &&
                          "Stays in sync with the map below — type an address to move the pin, or drag the pin to update the address."}
                      </p>
                      {errors.addressLine1 && (
                        <p className="text-xs text-destructive-text">{errors.addressLine1.message}</p>
                      )}
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                      <div className="space-y-1.5">
                        <Label htmlFor="addressLine2">Address line 2 (optional)</Label>
                        <Input id="addressLine2" placeholder="Suite, floor, unit" {...register("addressLine2")} />
                      </div>
                      <div className="space-y-1.5">
                        <Label htmlFor="postalCode">Postal code (optional)</Label>
                        <Input id="postalCode" {...register("postalCode")} />
                      </div>
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                      <div className="space-y-1.5">
                        <Label htmlFor="countryCode">Country</Label>
                        <Select
                          value={countryCodeValue}
                          onValueChange={(v) => {
                            countryManuallyEdited.current = true;
                            setValue("countryCode", v, { shouldValidate: true });
                          }}
                        >
                          <SelectTrigger id="countryCode" aria-invalid={!!errors.countryCode}>
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent className="max-h-72" showScrollbar>
                            {PHONE_COUNTRIES.map((c) => (
                              <SelectItem key={c.code} value={c.code}>
                                {flagEmoji(c.code)} {c.name}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        {errors.countryCode && (
                          <p className="text-xs text-destructive-text">{errors.countryCode.message}</p>
                        )}
                      </div>
                      <div className="space-y-1.5">
                        <Label htmlFor="currency">Currency</Label>
                        <CurrencySelect
                          id="currency"
                          value={currencyValue || null}
                          countryDefault={countryDefaultCurrency?.currency ?? null}
                          onChange={(code) => {
                            // Radix's hidden native-<select> autofill shim can fire a spurious
                            // onValueChange("") the first time `value` goes from unset to a real
                            // code — there's no real "blank currency" option in this list, so an
                            // empty callback value is always that shim, never a genuine pick.
                            if (!code) return;
                            currencyManuallyEdited.current = true;
                            setValue("currency", code, { shouldValidate: true });
                          }}
                          aria-invalid={!!errors.currency}
                          placeholder={currencyLoading ? "Checking…" : "Select a currency"}
                        />
                        {errors.currency && (
                          <p className="text-xs text-destructive-text">{errors.currency.message}</p>
                        )}
                      </div>
                    </div>
                    <p className="text-xs text-muted-foreground -mt-2">
                      Prices, deposits and payments in your studio use this currency. You can
                      change it until your first payment is recorded.
                    </p>

                    <div className="space-y-1.5">
                      <Label>Studio location</Label>
                      <LocationPicker
                        value={
                          !isNaN(latValue) && !isNaN(lngValue)
                            ? { lat: latValue, lng: lngValue, city: cityValue }
                            : undefined
                        }
                        onChange={({ lat, lng, city, streetAddress, countryCode }) => {
                          setValue("latitude",  lat,  { shouldValidate: true });
                          setValue("longitude", lng,  { shouldValidate: true });
                          setValue("city",      city, { shouldValidate: true });
                          if (!countryManuallyEdited.current && countryCode) {
                            setValue("countryCode", countryCode, { shouldValidate: true });
                          }
                          if (streetAddress) {
                            pinDrivenAddressUpdate.current = true;
                            setValue("addressLine1", streetAddress, { shouldValidate: true });
                          }
                        }}
                        error={
                          errors.latitude?.message ??
                          errors.longitude?.message ??
                          errors.city?.message
                        }
                      />
                    </div>

                    {serverError && (
                      <p className="text-sm text-destructive-text" role="alert">
                        {serverError}
                      </p>
                    )}

                    <Button type="button" className="w-full" onClick={handleNext}>
                      Next
                    </Button>
                  </>
                )}

                {step === 2 && (
                  <>
                    <div className="space-y-1.5">
                      <Label htmlFor="email">Email</Label>
                      <Input
                        id="email"
                        type="email"
                        autoComplete="email"
                        placeholder="owner@yourstudio.com"
                        readOnly={oauthProvider !== null}
                        className={oauthProvider !== null ? "bg-muted/40 cursor-default" : ""}
                        {...register("email")}
                        aria-invalid={!!errors.email}
                      />
                      {errors.email && (
                        <p className="text-xs text-destructive-text">{errors.email.message}</p>
                      )}
                    </div>

                    {oauthProvider === null && (
                      <>
                        <div className="space-y-1.5">
                          <Label htmlFor="password">Password</Label>
                          <PasswordInput
                            id="password"
                            autoComplete="new-password"
                            {...register("password")}
                            aria-invalid={!!errors.password}
                          />
                          {errors.password && (
                            <p className="text-xs text-destructive-text">{errors.password.message}</p>
                          )}
                          {(watch("password") !== "" || watch("confirmPassword") !== "") && (
                            <PasswordStrengthMeter password={watch("password")} />
                          )}
                        </div>

                        <div className="space-y-1.5">
                          <Label htmlFor="confirmPassword">Confirm password</Label>
                          <PasswordInput
                            id="confirmPassword"
                            autoComplete="new-password"
                            {...register("confirmPassword")}
                            aria-invalid={!!errors.confirmPassword}
                          />
                          {errors.confirmPassword && (
                            <p className="text-xs text-destructive-text">
                              {errors.confirmPassword.message}
                            </p>
                          )}
                        </div>
                      </>
                    )}

                    {oauthProvider !== null && (
                      <div className="flex items-center justify-between rounded-md border border-border/50 bg-muted/30 px-3 py-2">
                        <p className="text-xs text-muted-foreground capitalize">
                          Signing in with {oauthProvider}
                        </p>
                        <button
                          type="button"
                          onClick={() => { setOauthProvider(null); setOauthIdToken(null); }}
                          className="text-xs underline underline-offset-2 hover:text-foreground text-muted-foreground"
                        >
                          Change
                        </button>
                      </div>
                    )}

                    {oauthProvider === null && (
                      <OAuthButtons onToken={handleOAuthToken} disabled={isSubmitting} />
                    )}

                    {serverError && (
                      <p className="text-sm text-destructive-text" role="alert">
                        {serverError}
                      </p>
                    )}

                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant="outline"
                        className="flex-1"
                        onClick={() => setStep(1)}
                        disabled={isSubmitting}
                      >
                        Back
                      </Button>
                      <Button type="submit" className="flex-1" disabled={isSubmitting}>
                        {isSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                        Register
                      </Button>
                    </div>

                    <p className="mt-3 text-center text-xs text-muted-foreground">
                      By registering your studio you agree to our{" "}
                      <Link to="/terms" className="underline underline-offset-2 hover:text-foreground">
                        Terms of Service
                      </Link>{" "}
                      and{" "}
                      <Link to="/privacy" className="underline underline-offset-2 hover:text-foreground">
                        Privacy Policy
                      </Link>
                      .
                    </p>
                  </>
                )}
              </form>
              )}
            </CardContent>
          </Card>

          <p className="text-center text-sm text-muted-foreground">
            Already have an account?{" "}
            <Link
              to="/login"
              className="underline underline-offset-4 hover:text-primary"
            >
              Sign in
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
}
