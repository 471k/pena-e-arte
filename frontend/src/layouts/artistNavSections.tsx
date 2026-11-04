import {
  Bell, CalendarDays, CalendarCheck, DollarSign, FileText, ImagePlus, LayoutDashboard, MessageCircle,
  Palette, ScrollText, ShieldAlert, Users, Wallet, ListOrdered, Layers,
} from "lucide-react";
import type { NavItem, NavSection } from "@/shared/types/navItem";

interface ArtistNavOptions {
  scheduleHref:  string;
  designsHref:   string;
  reportsHref:   string;
  portfolioHref: string | null;
  /** Owner in artist mode: an escape hatch back to the owner dashboard, first in the menu. */
  ownerDashboard?: boolean;
  /** Standalone artists get a Notifications link; owners already have the header bell. */
  notifications?: boolean;
  /** Only ArtistLayout's onboarding tour targets these links. */
  tourIds?: boolean;
}

/**
 * The artist menu, shared by ArtistLayout and OwnerLayout's "artist mode" so an owner acting as an
 * artist sees exactly what a real invited artist sees (per the owner/artist context-scoped menu rule).
 */
export function buildArtistSections(opts: ArtistNavOptions): NavSection[] {
  const tour = (id: string): string | undefined => (opts.tourIds ? id : undefined);
  const icon = "h-4 w-4";

  const top: NavItem[] = [
    ...(opts.ownerDashboard
      ? [{ label: "Owner Dashboard", href: "/dashboard", icon: <LayoutDashboard className={icon} /> }]
      : []),
    // First: the daily entry point (E04a). It always shows the viewer's own sessions, so it needs no
    // ?artistId for an owner in artist mode.
    { label: "Today", href: "/today", icon: <CalendarCheck className={icon} />, tourId: tour("artist-today-nav") },
    { label: "Schedule", href: opts.scheduleHref, icon: <CalendarDays className={icon} />, tourId: tour("artist-schedule-nav") },
  ];

  const creative: NavItem[] = [
    { label: "Designs", href: opts.designsHref, icon: <Palette className={icon} /> },
    ...(opts.portfolioHref
      ? [{ label: "My Portfolio", href: opts.portfolioHref, icon: <ImagePlus className={icon} /> }]
      : []),
  ];

  const me: NavItem[] = [
    { label: "My Earnings", href: "/earnings", icon: <Wallet className={icon} />, tourId: tour("artist-earnings-nav") },
    { label: "Conduct Reports", href: opts.reportsHref, icon: <ShieldAlert className={icon} />, tourId: tour("artist-conduct-reports-nav") },
    ...(opts.notifications
      ? [{ label: "Notifications", href: "/notifications", icon: <Bell className={icon} /> }]
      : []),
  ];

  return [
    { id: "overview", entries: top },
    {
      id: "clients",
      label: "People",
      entries: [
        { label: "Clients",  href: "/clients",  icon: <Users className={icon} />,         tourId: tour("artist-clients-nav") },
        { label: "Messages", href: "/messages", icon: <MessageCircle className={icon} />, tourId: tour("artist-messages-nav") },
        { label: "Waitlist", href: "/waitlist", icon: <ListOrdered className={icon} />,   tourId: tour("artist-waitlist-nav") },
      ],
    },
    { id: "creative", label: "Creative", entries: creative },
    {
      id: "setup",
      label: "Setup",
      entries: [
        {
          id: "forms-rules",
          label: "Forms & rules",
          icon: <Layers className={icon} />,
          children: [
            { label: "Intake Forms",  href: "/forms/intake",  icon: <FileText className={icon} /> },
            { label: "Consent Forms", href: "/forms/consent", icon: <ScrollText className={icon} /> },
            { label: "Deposit Rules", href: "/deposit-rules", icon: <DollarSign className={icon} /> },
          ],
        },
      ],
    },
    { id: "me", label: "Me", entries: me },
  ];
}
