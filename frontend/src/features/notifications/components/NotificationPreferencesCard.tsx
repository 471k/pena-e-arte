import { useState, useEffect } from "react";
import { Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
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

const CHANNEL_LABELS: Record<NotificationChannel, string> = {
  Email: "Email",
  Sms:   "SMS",
};

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
                      {CHANNEL_LABELS[ch]}
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
                          aria-label={`${label} via ${CHANNEL_LABELS[channel]}`}
                        />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="sticky bottom-0 pt-2 pb-1 bg-card border-t -mx-6 px-6 mt-2">
          <Button
            size="sm"
            className="w-full gap-2"
            onClick={handleSave}
            disabled={saving || !dirty || isLoading}
          >
            {saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
            Save notification settings
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
