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
import { decodeToken } from "@/shared/utils/jwt";
import { useRegisterStudioMutation } from "../studiosApi";

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

const STEP_1_FIELDS = ["name", "slug", "city", "nipt", "latitude", "longitude"] as const;

const soloSchema = z.object({
  firstName: z.string().min(1, "First name is required").max(100),
  lastName:  z.string().min(1, "Last name is required").max(100),
  email:     z.string().min(1, "Email is required").max(256).email("Enter a valid email"),
  password:  z.string().min(8, "Password must be at least 8 characters"),
  confirmPassword: z.string(),
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

  const {
    register: registerSolo,
    handleSubmit: handleSoloSubmit,
    watch: watchSolo,
    formState: { errors: soloErrors },
  } = useForm<SoloFormValues>({
    resolver: zodResolver(soloSchema),
    defaultValues: { firstName: "", lastName: "", email: "", password: "", confirmPassword: "" },
  });

  async function onSoloSubmit(values: SoloFormValues) {
    setSoloServerError(null);
    try {
      await registerSoloArtist({
        firstName: values.firstName,
        lastName:  values.lastName,
        email:     values.email,
        password:  values.password,
      }).unwrap();

      const { accessToken } = await login({
        email: values.email,
        password: values.password,
      }).unwrap();

      dispatch(setCredentials(decodeToken(accessToken)));
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
        latitude:     values.latitude,
        longitude:    values.longitude,
        ownerEmail:   values.email,
        ...(pendingReferralCode ? { referralCode: pendingReferralCode } : {}),
      }).unwrap();

      if (oauthProvider && oauthIdToken) {
        await oauthRegister({
          provider: oauthProvider,
          idToken:  oauthIdToken,
          role:     "owner",
          studioId: studio.id,
        }).unwrap();

        const { accessToken } = await oauthLogin({
          provider: oauthProvider,
          idToken:  oauthIdToken,
        }).unwrap();

        dispatch(setCredentials(decodeToken(accessToken)));
      } else {
        await registerUser({
          email: values.email,
          password: values.password,
          role: "owner",
          studioId: studio.id,
        }).unwrap();

        const { accessToken } = await login({
          email: values.email,
          password: values.password,
        }).unwrap();

        dispatch(setCredentials(decodeToken(accessToken)));
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
                        <p className="text-xs text-destructive">{soloErrors.firstName.message}</p>
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
                        <p className="text-xs text-destructive">{soloErrors.lastName.message}</p>
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
                      <p className="text-xs text-destructive">{soloErrors.email.message}</p>
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
                      <p className="text-xs text-destructive">{soloErrors.password.message}</p>
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
                      <p className="text-xs text-destructive">{soloErrors.confirmPassword.message}</p>
                    )}
                  </div>

                  <p className="text-xs text-muted-foreground">
                    You'll be able to take bookings right away. Add your studio's business
                    details, or make it visible on the map, any time from Settings.
                  </p>

                  {soloServerError && (
                    <p className="text-sm text-destructive" role="alert">
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
                        <p className="text-xs text-destructive">{errors.name.message}</p>
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
                        <p className="text-xs text-destructive">{errors.slug.message}</p>
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
                        <p className="text-xs text-destructive">{errors.nipt.message}</p>
                      )}
                    </div>

                    <div className="space-y-1.5">
                      <Label>Studio location</Label>
                      <LocationPicker
                        value={
                          !isNaN(latValue) && !isNaN(lngValue)
                            ? { lat: latValue, lng: lngValue, city: cityValue }
                            : undefined
                        }
                        onChange={({ lat, lng, city }) => {
                          setValue("latitude",  lat,  { shouldValidate: true });
                          setValue("longitude", lng,  { shouldValidate: true });
                          setValue("city",      city, { shouldValidate: true });
                        }}
                        error={
                          errors.latitude?.message ??
                          errors.longitude?.message ??
                          errors.city?.message
                        }
                      />
                    </div>

                    {serverError && (
                      <p className="text-sm text-destructive" role="alert">
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
                        <p className="text-xs text-destructive">{errors.email.message}</p>
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
                            <p className="text-xs text-destructive">{errors.password.message}</p>
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
                            <p className="text-xs text-destructive">
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
                      <p className="text-sm text-destructive" role="alert">
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
