import { useState } from "react";
import { Link } from "react-router-dom";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { parsePhoneNumberFromString } from "libphonenumber-js/min";
import { toast } from "sonner";
import { Loader2, Pencil } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { isValidE164Phone, PHONE_ERROR_MESSAGE } from "@/shared/utils/phoneValidation";
import { useUpdateMyClientMutation, type ClientResponse } from "../clientsApi";

const schema = z.object({
  firstName: z.string().trim().min(1, "First name is required").max(100, "Keep it under 100 characters"),
  lastName:  z.string().trim().min(1, "Last name is required").max(100, "Keep it under 100 characters"),
  phone:     z.string().refine(isValidE164Phone, PHONE_ERROR_MESSAGE),
});

type FormValues = z.infer<typeof schema>;

/**
 * The API accepts strict E.164 only (`+351912111222`). Numbers saved before PhoneInput existed can be
 * valid but spaced (`+351 912 111 222`), so normalise on the way out; blank means "remove my number".
 */
function toE164(raw: string): string | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  return parsePhoneNumberFromString(trimmed)?.number ?? trimmed;
}

function ProfileField({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <div className="space-y-0.5">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-sm font-medium">{value ? value : <span className="text-muted-foreground">—</span>}</p>
    </div>
  );
}

/**
 * The client's own name / email / phone. Name and phone are editable; email is the login identity,
 * so it stays read-only here and points at the confirmed change-email flow. The backend applies the
 * edit to every studio the client belongs to (one Client row per studio), so it's a person-level
 * change — not just the studio they happen to be viewing.
 */
export function ContactDetailsCard({ client }: { client: ClientResponse }) {
  const [editing, setEditing] = useState(false);
  const [updateMyClient, { isLoading }] = useUpdateMyClientMutation();

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: client.firstName, lastName: client.lastName, phone: client.phone ?? "" },
  });

  function startEditing() {
    // Re-seed from the latest server data every time — the query may have refetched since mount.
    reset({ firstName: client.firstName, lastName: client.lastName, phone: client.phone ?? "" });
    setEditing(true);
  }

  async function onSubmit(values: FormValues) {
    const result = await updateMyClient({
      firstName: values.firstName.trim(),
      lastName:  values.lastName.trim(),
      phone:     toE164(values.phone),
    });
    if ("error" in result) {
      toast.error("Failed to save your details.");
      return;
    }
    toast.success("Your details were saved.");
    setEditing(false);
  }

  return (
    <Card>
      <CardHeader className="pb-3 flex-row items-center justify-between space-y-0">
        <CardTitle className="text-sm font-medium">Contact</CardTitle>
        {!editing && (
          <Button
            variant="ghost"
            size="sm"
            onClick={startEditing}
            aria-label="Edit contact details"
            className="h-7 gap-1 text-xs px-2"
          >
            <Pencil className="h-3 w-3" />
            Edit
          </Button>
        )}
      </CardHeader>

      <CardContent>
        {!editing ? (
          <div className="space-y-3">
            <ProfileField label="Email" value={client.email} />
            <ProfileField label="Phone" value={client.phone} />
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="contact-first-name">First name</Label>
                <Input
                  id="contact-first-name"
                  autoComplete="given-name"
                  aria-invalid={!!errors.firstName}
                  aria-describedby={errors.firstName ? "contact-first-name-error" : undefined}
                  {...register("firstName")}
                />
                {errors.firstName && (
                  <p id="contact-first-name-error" className="text-xs text-destructive-text">
                    {errors.firstName.message}
                  </p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="contact-last-name">Last name</Label>
                <Input
                  id="contact-last-name"
                  autoComplete="family-name"
                  aria-invalid={!!errors.lastName}
                  aria-describedby={errors.lastName ? "contact-last-name-error" : undefined}
                  {...register("lastName")}
                />
                {errors.lastName && (
                  <p id="contact-last-name-error" className="text-xs text-destructive-text">
                    {errors.lastName.message}
                  </p>
                )}
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="phone">Phone (optional)</Label>
              <Controller
                control={control}
                name="phone"
                render={({ field }) => (
                  <PhoneInput
                    id="phone"
                    value={field.value ?? ""}
                    onChange={field.onChange}
                    onBlur={field.onBlur}
                    aria-invalid={!!errors.phone}
                    aria-describedby={errors.phone ? "contact-phone-error" : undefined}
                  />
                )}
              />
              {errors.phone && (
                <p id="contact-phone-error" className="text-xs text-destructive-text">{errors.phone.message}</p>
              )}
              <p className="text-xs text-muted-foreground">
                Used for appointment reminders. Clear it to remove your number.
              </p>
            </div>

            <div className="space-y-0.5">
              <p className="text-xs text-muted-foreground">Email</p>
              <p className="text-sm font-medium">{client.email}</p>
              <p className="text-xs text-muted-foreground">
                Your email is your sign-in address.{" "}
                <Link to="/account/change-email" className="underline hover:text-foreground">
                  Change email
                </Link>
              </p>
            </div>

            <p className="text-xs text-muted-foreground">
              Changes apply to your profile at every studio you&apos;re a client at.
            </p>

            <div className="flex items-center justify-end gap-2">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => setEditing(false)}
                disabled={isLoading}
              >
                Cancel
              </Button>
              <Button type="submit" size="sm" disabled={isLoading}>
                {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Save"}
              </Button>
            </div>
          </form>
        )}
      </CardContent>
    </Card>
  );
}
