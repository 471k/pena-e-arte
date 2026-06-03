import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, PenLine } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate } from "react-router-dom";
import { z } from "zod";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { getRoleRedirectPath } from "@/app/router";
import { useLoginMutation, useRegisterUserMutation } from "@/features/auth/authApi";
import { setCredentials } from "@/features/auth/authSlice";
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
    password: z.string().min(8, "Password must be at least 8 characters"),
    confirmPassword: z.string().min(1, "Confirm your password"),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: "Passwords do not match",
    path: ["confirmPassword"],
  });

type FormValues = z.infer<typeof schema>;

const STEP_1_FIELDS = ["name", "slug", "city", "latitude", "longitude"] as const;

export function RegisterStudioPage() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const existingRole = useAppSelector((s) => s.auth.role);

  const [step, setStep] = useState<1 | 2>(1);
  const [serverError, setServerError] = useState<string | null>(null);
  const slugManuallyEdited = useRef(false);

  const [registerStudio] = useRegisterStudioMutation();
  const [registerUser] = useRegisterUserMutation();
  const [login] = useLoginMutation();

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
      latitude: NaN,
      longitude: NaN,
      email: "",
      password: "",
      confirmPassword: "",
    },
  });

  const nameValue = watch("name");
  const slugValue = watch("slug");

  useEffect(() => {
    if (existingRole) {
      navigate(getRoleRedirectPath(existingRole), { replace: true });
    }
  }, [existingRole, navigate]);

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

  async function onSubmit(values: FormValues) {
    setServerError(null);
    try {
      const studio = await registerStudio({
        name: values.name,
        slug: values.slug,
        city: values.city,
        latitude: values.latitude,
        longitude: values.longitude,
        ownerEmail: values.email,
      }).unwrap();

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
      navigate("/dashboard", { replace: true });
    } catch (err) {
      const message =
        typeof err === "object" && err !== null && "data" in err
          ? ((err as { data: { detail?: string } }).data?.detail ??
            "Registration failed. Please try again.")
          : "Unable to reach the server. Please try again.";
      setServerError(message);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="w-full max-w-md space-y-6">
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex items-center gap-2">
            <PenLine className="h-8 w-8" />
            <span className="text-2xl font-semibold tracking-tight">Pena e Arte</span>
          </div>
          <p className="text-sm text-muted-foreground">Tattoo Studio Management</p>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Register your studio</CardTitle>
            <CardDescription>
              Step {step} of 2 — {step === 1 ? "Studio details" : "Owner account"}
            </CardDescription>
          </CardHeader>
          <CardContent>
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
                      penaearte.com/
                      <strong>{slugValue || "your-slug"}</strong>
                    </p>
                    {errors.slug && (
                      <p className="text-xs text-destructive">{errors.slug.message}</p>
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <Label htmlFor="city">City</Label>
                    <Input
                      id="city"
                      placeholder="Lisbon"
                      {...register("city")}
                      aria-invalid={!!errors.city}
                    />
                    {errors.city && (
                      <p className="text-xs text-destructive">{errors.city.message}</p>
                    )}
                  </div>

                  <div className="grid grid-cols-2 gap-3">
                    <div className="space-y-1.5">
                      <Label htmlFor="latitude">Latitude</Label>
                      <Input
                        id="latitude"
                        type="number"
                        step="any"
                        placeholder="38.7169"
                        {...register("latitude", { valueAsNumber: true })}
                        aria-invalid={!!errors.latitude}
                      />
                      {errors.latitude && (
                        <p className="text-xs text-destructive">{errors.latitude.message}</p>
                      )}
                    </div>
                    <div className="space-y-1.5">
                      <Label htmlFor="longitude">Longitude</Label>
                      <Input
                        id="longitude"
                        type="number"
                        step="any"
                        placeholder="-9.1395"
                        {...register("longitude", { valueAsNumber: true })}
                        aria-invalid={!!errors.longitude}
                      />
                      {errors.longitude && (
                        <p className="text-xs text-destructive">{errors.longitude.message}</p>
                      )}
                    </div>
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
                      {...register("email")}
                      aria-invalid={!!errors.email}
                    />
                    {errors.email && (
                      <p className="text-xs text-destructive">{errors.email.message}</p>
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <Label htmlFor="password">Password</Label>
                    <Input
                      id="password"
                      type="password"
                      autoComplete="new-password"
                      {...register("password")}
                      aria-invalid={!!errors.password}
                    />
                    {errors.password && (
                      <p className="text-xs text-destructive">{errors.password.message}</p>
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <Label htmlFor="confirmPassword">Confirm password</Label>
                    <Input
                      id="confirmPassword"
                      type="password"
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
                </>
              )}
            </form>
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
  );
}
