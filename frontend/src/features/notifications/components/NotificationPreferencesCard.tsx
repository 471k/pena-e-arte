import { useState, useEffect } from "react";
import { Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import {
  useGetNotificationPreferencesQuery,
  useUpdateNotificationPreferencesMutation,
} from "../notificationsApi";
import type { NotificationChannel, NotificationPreferenceItem, NotificationType } from "../notification.types";

const NOTIFICATION_TYPES: { value: NotificationType; label: string }[] = [
  { value: "AppointmentCreated",    label: "Appointment created" },
  { value: "AppointmentConfirmed",  label: "Appointment confirmed" },
  { value: "AppointmentCancelled",  label: "Appointment cancelled" },
  { value: "DepositCaptured",       label: "Deposit captured" },
  { value: "PaymentRefunded",       label: "Payment refunded" },
  { value: "IntakeFormSubmitted",   label: "Intake form submitted" },
  { value: "ConsentFormSigned",     label: "Consent form signed" },
  { value: "DesignReviewed",        label: "Design reviewed" },
];

const CHANNELS: NotificationChannel[] = ["Email", "Sms"];

type PreferenceMap = Record<string, boolean>;

function prefKey(type: NotificationType, channel: NotificationChannel) {
  return `${type}:${channel}`;
}

function buildMap(items: NotificationPreferenceItem[]): PreferenceMap {
  const map: PreferenceMap = {};
  for (const item of items) {
    map[prefKey(item.type, item.channel)] = item.isEnabled;
  }
  return map;
}

function ToggleSwitch({ checked, onChange }: { checked: boolean; onChange: () => void }) {
  return (
    <button
      role="switch"
      aria-checked={checked}
      onClick={onChange}
      className={[
        "relative inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full",
        "border-2 border-transparent transition-colors focus-visible:outline-none",
        "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
        checked ? "bg-primary" : "bg-input",
      ].join(" ")}
    >
      <span
        className={[
          "pointer-events-none block h-4 w-4 rounded-full bg-background shadow-lg",
          "ring-0 transition-transform",
          checked ? "translate-x-4" : "translate-x-0",
        ].join(" ")}
      />
    </button>
  );
}

export function NotificationPreferencesCard() {
  const { data, isLoading } = useGetNotificationPreferencesQuery();
  const [update, { isLoading: saving }] = useUpdateNotificationPreferencesMutation();

  const [local, setLocal] = useState<PreferenceMap>({});
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (data) {
      setLocal(buildMap(data.preferences));
      setDirty(false);
    }
  }, [data]);

  function toggle(type: NotificationType, channel: NotificationChannel) {
    const key = prefKey(type, channel);
    setLocal((prev) => ({ ...prev, [key]: !prev[key] }));
    setDirty(true);
  }

  async function handleSave() {
    const preferences: NotificationPreferenceItem[] = NOTIFICATION_TYPES.flatMap(({ value: type }) =>
      CHANNELS.map((channel) => ({
        type,
        channel,
        isEnabled: local[prefKey(type, channel)] ?? true,
      }))
    );
    try {
      await update(preferences).unwrap();
      setDirty(false);
      toast.success("Notification preferences saved.");
    } catch {
      toast.error("Failed to save preferences.");
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Notification preferences</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          Choose which notifications are sent when studio events occur.
        </p>

        {isLoading ? (
          <div className="flex items-center gap-2 text-muted-foreground text-sm">
            <Loader2 className="h-4 w-4 animate-spin" />
            Loading…
          </div>
        ) : (
          <div className="rounded-md border overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b bg-muted/40">
                  <th className="text-left px-3 py-2 font-medium text-muted-foreground w-full">
                    Event
                  </th>
                  {CHANNELS.map((ch) => (
                    <th key={ch} className="px-3 py-2 font-medium text-muted-foreground text-center whitespace-nowrap">
                      {ch}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {NOTIFICATION_TYPES.map(({ value: type, label }, i) => (
                  <tr
                    key={type}
                    className={i % 2 === 0 ? undefined : "bg-muted/20"}
                  >
                    <td className="px-3 py-2.5 text-foreground">{label}</td>
                    {CHANNELS.map((channel) => (
                      <td key={channel} className="px-3 py-2.5 text-center">
                        <ToggleSwitch
                          checked={local[prefKey(type, channel)] ?? true}
                          onChange={() => toggle(type, channel)}
                        />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <Button
          size="sm"
          className="gap-2"
          onClick={handleSave}
          disabled={saving || !dirty || isLoading}
        >
          {saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
          Save preferences
        </Button>
      </CardContent>
    </Card>
  );
}
