import { useEffect, type ReactNode } from "react";
import { NotificationPreferencesCard } from "@/features/notifications/components/NotificationPreferencesCard";
import { ALWAYS_SHOWN_GROUP, SETTINGS_GROUPS, settingsGroupElementId, type SettingsGroupId } from "../settingsGroups";
import type { SettingsGroupsController } from "../useSettingsGroups";
import { SettingsGroup } from "./SettingsGroup";
import { CurrencySettingsCard } from "./CurrencySettingsCard";
import { BrandingSettingsCard } from "./BrandingSettingsCard";
import { DeveloperSettingsCard } from "./DeveloperSettingsCard";
import { WebhookSettingsCard } from "./WebhookSettingsCard";
import { PokSettingsCard } from "./PokSettingsCard";
import { QrCodeSection } from "./QrCodeSection";
import { ReferralCodeCard } from "./ReferralCodeCard";
import { StudioClosuresCard } from "./StudioClosuresCard";
import { StudioHoursCard } from "./StudioHoursCard";
import { StudioPayModelCard } from "./StudioPayModelCard";
import { StudioAuditLogCard } from "./StudioAuditLogCard";
import { StudioSocialLinksCard } from "./StudioSocialLinksCard";
import { EmbedCodeCard } from "./EmbedCodeCard";

interface StudioSettingsSectionsProps {
  /** The studio's own URL and details form, which StudioProfilePage owns; it heads Business details. */
  profile: ReactNode;
  groups:  SettingsGroupsController;
}

/**
 * Where each Studio Settings card lives (E05c, O04). Each card is the same component it was in the flat
 * stack, moved, not rewritten: it loads and saves on its own, as before.
 */
export function StudioSettingsSections({ profile, groups }: StudioSettingsSectionsProps) {
  // Imperative scroll to the group a link named (a query parameter isn't a fragment the browser scrolls
  // to). Runs when the sections first appear and on each later navigation that names a group.
  const scrollId = groups.scrollTarget?.id;
  const scrollKey = groups.scrollTarget?.navigationKey;
  useEffect(() => {
    if (!scrollId) return;
    document.getElementById(settingsGroupElementId(scrollId))?.scrollIntoView({ block: "start" });
  }, [scrollId, scrollKey]);

  const content: Record<SettingsGroupId, ReactNode> = {
    profile: (
      <>
        {profile}
        <StudioSocialLinksCard />
      </>
    ),
    hours: (
      <>
        <StudioHoursCard />
        <StudioClosuresCard />
      </>
    ),
    money: (
      <>
        <CurrencySettingsCard />
        <PokSettingsCard />
        <StudioPayModelCard />
      </>
    ),
    booking: (
      <>
        <EmbedCodeCard />
        <BrandingSettingsCard />
        <QrCodeSection />
      </>
    ),
    notifications: <NotificationPreferencesCard />,
    integrations: (
      <>
        <DeveloperSettingsCard />
        <WebhookSettingsCard />
      </>
    ),
    referrals: <ReferralCodeCard />,
    activity: <StudioAuditLogCard />,
  };

  const shown = SETTINGS_GROUPS.find((g) => g.id === ALWAYS_SHOWN_GROUP)!;
  const collapsible = SETTINGS_GROUPS.filter((g) => g.id !== ALWAYS_SHOWN_GROUP);

  return (
    <>
      <SettingsGroup id={shown.id} title={shown.title} summary={shown.summary} open collapsible={false}>
        {content[shown.id]}
      </SettingsGroup>
      <div className="rounded-xl border bg-card divide-y divide-border">
        {collapsible.map((g) => (
          <SettingsGroup
            key={g.id}
            id={g.id}
            title={g.title}
            summary={g.summary}
            open={groups.isOpen(g.id)}
            onToggle={() => groups.toggle(g.id)}
          >
            {content[g.id]}
          </SettingsGroup>
        ))}
      </div>
    </>
  );
}
