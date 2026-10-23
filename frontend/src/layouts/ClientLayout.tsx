import { Outlet, useNavigate } from "react-router-dom";
import {
  CalendarDays, Palette, FileText, ScrollText, User, PenLine, Building2, MessageCircle,
  ListOrdered, Package as PackageIcon, CreditCard, Layers,
} from "lucide-react";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserMenu } from "@/shared/components/UserMenu";
import { NavDrawer } from "@/shared/components/NavDrawer";
import { AppSidebar } from "@/shared/components/AppSidebar";
import { SidebarToggle } from "@/shared/components/SidebarToggle";
import { useNavShell } from "@/shared/hooks/useNavShell";
import type { NavSection } from "@/shared/types/navItem";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { NotificationBell } from "@/features/notifications";
import { HelpMenu } from "@/features/help";
import { MessagesNavBadge, useChatHub } from "@/features/messaging";
import { useGetPaymentCapabilitiesQuery } from "@/features/payments/paymentsApi";

const ICON = "h-4 w-4";

const NAV_SECTIONS: NavSection[] = [
  {
    id: "overview",
    entries: [
      { label: "Book Appointment", href: "/book", icon: <CalendarDays className={ICON} />, tourId: "client-book-nav" },
    ],
  },
  {
    id: "studios",
    label: "Studios",
    entries: [
      { label: "My Studios", href: "/my-studios", icon: <Building2 className={ICON} />,     tourId: "client-my-studios-nav" },
      { label: "Messages",   href: "/messages",   icon: <MessageCircle className={ICON} />, tourId: "client-messages-nav" },
    ],
  },
  {
    id: "my-tattoos",
    label: "My tattoos",
    entries: [
      { label: "My Waitlist", href: "/waitlist/mine", icon: <ListOrdered className={ICON} />, tourId: "client-waitlist-nav" },
      { label: "My Designs",  href: "/designs",       icon: <Palette className={ICON} />,     tourId: "client-designs-nav" },
      { label: "Packages",    href: "/packages/buy",  icon: <PackageIcon className={ICON} /> },
      {
        id: "forms", label: "Forms", icon: <Layers className={ICON} />,
        children: [
          { label: "Intake Forms",  href: "/forms/intake",  icon: <FileText className={ICON} /> },
          { label: "Consent Forms", href: "/forms/consent", icon: <ScrollText className={ICON} /> },
        ],
      },
    ],
  },
  {
    id: "account",
    label: "Account",
    entries: [
      {
        id: "my-account", label: "My account", icon: <User className={ICON} />,
        children: [
          // `end` so "My Profile" doesn't also light up on /clients/me/payment-methods.
          { label: "My Profile",      href: "/clients/me",                 icon: <User className={ICON} />, end: true },
          // Shown only while saved cards are on — see NAV_SECTIONS_WITHOUT_SAVED_CARDS below.
          { label: "Payment Methods", href: "/clients/me/payment-methods", icon: <CreditCard className={ICON} />, tourId: "client-payment-methods-nav" },
        ],
      },
    ],
  },
];

const PAYMENT_METHODS_HREF = "/clients/me/payment-methods";

/** NAV_SECTIONS without "Payment Methods" — the menu while saved cards are switched off
 * (Payments:SavedCardsEnabled, backlog C-9) and while that is still loading. */
const NAV_SECTIONS_WITHOUT_SAVED_CARDS: NavSection[] = NAV_SECTIONS.map((section) => ({
  ...section,
  entries: section.entries.map((entry) =>
    "children" in entry
      ? { ...entry, children: entry.children.filter((child) => child.href !== PAYMENT_METHODS_HREF) }
      : entry,
  ),
}));

export function ClientLayout() {
  const dispatch  = useAppDispatch();
  const navigate  = useNavigate();
  const tenantId  = useAppSelector((s) => s.auth.tenantId);
  const { data: capabilities } = useGetPaymentCapabilitiesQuery(undefined, { skip: !tenantId });
  const savedCardsAvailable = capabilities?.savedCardsAvailable === true;
  const sections = savedCardsAvailable ? NAV_SECTIONS : NAV_SECTIONS_WITHOUT_SAVED_CARDS;
  useSignalR(tenantId);
  useChatHub();
  const { navOpen, setNavOpen, revealTourId, onBeforeTourStep, onTourEnd, navModal } = useNavShell();

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner role="client" />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <header className="flex items-center gap-2 px-4 sm:px-6 h-14 border-b bg-background sticky top-0 z-20">
        <SidebarToggle />
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight max-[359px]:hidden">TattooOS</span>

        <NavDrawer sections={sections} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} revealTourId={revealTourId} modal={navModal} />

        <div className="ml-auto flex items-center gap-1 sm:gap-3">
          <HelpMenu
            onBeforeTourStep={onBeforeTourStep}
            onTourEnd={onTourEnd}
            showPaymentMethodsTourStep={savedCardsAvailable}
          />
          <MessagesNavBadge />
          <NotificationBell />
          <UserMenu onLogout={handleLogout} />
        </div>
      </header>

      <div className="flex flex-1 min-h-0">
        <AppSidebar sections={sections} revealTourId={revealTourId} />
        <div className="flex-1 min-w-0">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
