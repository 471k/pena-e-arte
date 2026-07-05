import { useEffect, useState } from "react";
import { Loader2, Save }       from "lucide-react";
import { toast }               from "sonner";
import { Button }              from "@/shared/components/ui/button";
import { ToggleSwitch }        from "@/shared/components/ui/toggle-switch";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription,
} from "@/shared/components/ui/sheet";
import {
  useGetClientStudioNotificationPreferencesQuery,
  useUpdateClientStudioNotificationPreferencesMutation,
} from "@/features/auth/authApi";
import type { ClientNotificationPreferenceItem } from "@/features/auth/authApi";

type NotificationChannel = "Email" | "Sms";

const CLIENT_NOTIFICATION_TYPES: { value: string; label: string }[] = [
  { value: "AppointmentCreated",   label: "Appointment confirmed" },
  { value: "AppointmentConfirmed", label: "Appointment reminder" },
  { value: "AppointmentCancelled", label: "Appointment cancelled" },
  { value: "DepositCaptured",      label: "Deposit captured" },
  { value: "PaymentRefunded",      label: "Payment refunded" },
];

const CHANNELS: NotificationChannel[] = ["Email", "Sms"];
const CHANNEL_LABELS: Record<NotificationChannel, string> = { Email: "Email", Sms: "SMS" };

type PreferenceMap = Record<string, boolean>;

function prefKey(type: string, channel: string) {
  return `${type}:${channel}`;
}

function buildMap(items: ClientNotificationPreferenceItem[]): PreferenceMap {
  const map: PreferenceMap = {};
  for (const item of items) {
    map[prefKey(item.type, item.channel)] = item.isEnabled;
  }
  return map;
}

interface Props {
  studioId:   string;
  studioName: string;
  open:       boolean;
  onClose:    () => void;
}

export function StudioNotificationSheet({ studioId, studioName, open, onClose }: Props) {
  const { data, isLoading } = useGetClientStudioNotificationPreferencesQuery(
    { studioId },
    { skip: !open },
  );
  const [update, { isLoading: saving }] = useUpdateClientStudioNotificationPreferencesMutation();

  const [local, setLocal] = useState<PreferenceMap>({});
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (data) {
      setLocal(buildMap(data.preferences));
      setDirty(false);
    }
  }, [data]);

  function toggle(type: string, channel: NotificationChannel) {
    const key = prefKey(type, channel);
    setLocal((prev) => ({ ...prev, [key]: !prev[key] }));
    setDirty(true);
  }

  async function handleSave() {
    const preferences: ClientNotificationPreferenceItem[] =
      CLIENT_NOTIFICATION_TYPES.flatMap(({ value: type }) =>
        CHANNELS.map((channel) => ({
          type,
          channel,
          isEnabled: local[prefKey(type, channel)] ?? true,
        }))
      );
    try {
      await update({ studioId, preferences }).unwrap();
      setDirty(false);
      toast.success("Notification preferences saved.");
      onClose();
    } catch {
      toast.error("Failed to save preferences.");
    }
  }

  return (
    <Sheet open={open} onOpenChange={(o) => !o && onClose()}>
      <SheetContent
        side="right"
        className="w-full sm:max-w-md flex flex-col"
        onOpenAutoFocus={(event) => event.preventDefault()}
      >
        <SheetHeader>
          <SheetTitle>Notifications — {studioName}</SheetTitle>
          <SheetDescription>
            Control which notifications this studio sends you.
          </SheetDescription>
        </SheetHeader>

        <div className="flex-1 overflow-y-auto mt-4">
          {isLoading ? (
            <div className="flex items-center gap-2 text-muted-foreground text-sm py-8 justify-center">
              <Loader2 className="h-4 w-4 animate-spin" />
              Loading…
            </div>
          ) : (
            <div className="rounded-md border overflow-hidden">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b bg-muted/40">
                    <th className="text-left px-3 py-2 font-medium text-muted-foreground w-full">
                      Notification
                    </th>
                    {CHANNELS.map((ch) => (
                      <th key={ch} className="px-3 py-2 font-medium text-muted-foreground text-center whitespace-nowrap">
                        {CHANNEL_LABELS[ch]}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {CLIENT_NOTIFICATION_TYPES.map(({ value: type, label }, i) => (
                    <tr key={type} className={i % 2 === 0 ? undefined : "bg-muted/20"}>
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
        </div>

        <div className="border-t pt-4 pb-2">
          <Button
            className="w-full gap-2"
            onClick={handleSave}
            disabled={saving || !dirty || isLoading}
          >
            {saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
            Save preferences
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
