import { useEffect } from "react";
import { Navigate, Outlet, createBrowserRouter, useNavigate } from "react-router-dom";
import { LoginPage, ForgotPasswordPage, ResetPasswordPage, ChangePasswordPage, RequestChangeEmailPage, ConfirmChangeEmailPage, VerifyEmailPage, ClientRegisterPage, MyStudiosPage } from "@/features/auth";
import { RegisterStudioPage, StudioProfilePage } from "@/features/studios";
import { BillingPage, SubscribePage } from "@/features/billing";
import { DashboardPage } from "@/features/dashboard";
import { StudioMapPage } from "@/features/map";
import { SchedulePage, BookPage, AppointmentDetailPage } from "@/features/appointments";
import { ArtistListPage, ArtistDetailPage, CreateArtistPage } from "@/features/artists";
import { ClientListPage, CreateClientPage, ClientDetailPage, MyProfilePage, TattooRecordDetailPage } from "@/features/clients";
import { DesignListPage, CreateDesignPage, UploadRevisionPage, DesignDetailPage } from "@/features/designs";
import {
  SubmitIntakeFormPage,
  IntakeFormListPage,
  IntakeFormBuilderPage,
  IntakeFormDetailPage,
  SignConsentFormPage,
  ConsentFormListPage,
  ConsentFormDetailPage,
} from "@/features/forms";
import { DepositRuleListPage, DepositRuleDetailPage, CreateDepositRulePage } from "@/features/deposit-rules";
import { PromoCodeListPage, PromoCodeDetailPage, CreatePromoCodePage } from "@/features/promo-codes";
import { ReportsPage, MyEarningsPage } from "@/features/reports";
import { NotificationLogListPage } from "@/features/notifications";
import { PaymentListPage, PaymentDetailPage, CreatePaymentIntentPage, DepositCheckoutPage } from "@/features/payments";
import {
  AdminDashboardPage,
  AdminStudioListPage,
  AdminStudioDetailPage,
  PlanManagementPage,
  PlanEditPage,
  SubscriptionOversightPage,
  PlatformReferralPage,
  IndustryReportsPage,
  HelpInsightsPage,
  AuditLogPage,
  LiveTrafficPage,
} from "@/features/platform";
import { FeedbackInboxPage } from "@/features/feedback";
import { StudioPortfolioPage, ArtistPortfolioPage, SharedDesignPage, EmbedPage, DiscoverPage, HomePage, PrivacyPolicyPage, TermsOfServicePage, RefundPolicyPage, ContactPage, UnsubscribePage } from "@/features/public";
import { ConductReportsPage, ConductReportInboxPage } from "@/features/conduct-reports";
import { MessagesInboxPage } from "@/features/messaging";
import { CampaignsPage } from "@/features/campaigns";
import { ErrorBoundary } from "@/shared/components/ErrorBoundary";
import { ImpersonationBanner } from "@/shared/components/ImpersonationBanner";
import { ClientLayout } from "@/layouts/ClientLayout";
import { ArtistLayout } from "@/layouts/ArtistLayout";
import { OwnerLayout } from "@/layouts/OwnerLayout";
import { AdminLayout } from "@/layouts/AdminLayout";
import { Role } from "@/shared/types/roles";
import { logout } from "@/features/auth/authSlice";
import { clearSessionExpired, clearImpersonationSessionExpired } from "@/features/ui/uiSlice";
import { useAppDispatch, useAppSelector } from "./hooks";
import { toast } from "sonner";

export function RoleGuard({ allowedRoles }: { allowedRoles: Role[] }) {
  const role = useAppSelector((s) => s.auth.role);
  const impersonating = useAppSelector((s) => s.auth.impersonation !== null);

  if (!role) return <Navigate to="/login" replace />;
  if (!allowedRoles.includes(role)) return <Navigate to={getRoleRedirectPath(role, impersonating)} replace />;

  return <Outlet />;
}

// While an admin holds an active Support Impersonation session, their effective "home" is
// the studio they're viewing (the owner dashboard), not the platform admin console — the
// whole point of the session is to browse studio-scoped pages. The "admin" role itself
// never changes (see AuthorizationExtensions.cs), so this is the one place that distinction
// has to be threaded through explicitly.
export function getRoleRedirectPath(role: Role, impersonating = false): string {
  if (role === Role.Admin && impersonating) return "/dashboard";
  switch (role) {
    case Role.Client: return "/book";
    case Role.Artist: return "/schedule";
    case Role.Owner: return "/dashboard";
    case Role.Admin: return "/platform";
  }
}

function IndexRedirect() {
  const role = useAppSelector((s) => s.auth.role);
  const impersonating = useAppSelector((s) => s.auth.impersonation !== null);
  // Unauthenticated root visit now lands on the public Home surface (PENA-102)
  // instead of being bounced straight into /discover. Authenticated users still
  // go to their role home.
  if (!role) return <HomePage />;
  return <Navigate to={getRoleRedirectPath(role, impersonating)} replace />;
}

function CatchAllRedirect() {
  const role = useAppSelector((s) => s.auth.role);
  const impersonating = useAppSelector((s) => s.auth.impersonation !== null);
  if (!role) return <Navigate to="/discover" replace />;
  return <Navigate to={getRoleRedirectPath(role, impersonating)} replace />;
}

export function AppRoot() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const sessionExpired = useAppSelector((s) => s.ui.sessionExpired);
  const impersonationSessionExpired = useAppSelector((s) => s.ui.impersonationSessionExpired);

  useEffect(() => {
    if (sessionExpired) {
      dispatch(clearSessionExpired());
      dispatch(logout());
      navigate("/login?reason=session_expired", { replace: true });
    }
  }, [sessionExpired, dispatch, navigate]);

  useEffect(() => {
    if (impersonationSessionExpired) {
      dispatch(clearImpersonationSessionExpired());
      toast.info("Impersonation session ended — you're back in your own admin session.");
      navigate("/platform", { replace: true });
    }
  }, [impersonationSessionExpired, dispatch, navigate]);

  return (
    <>
      <ImpersonationBanner />
      <Outlet />
    </>
  );
}

function AppLayout() {
  const role = useAppSelector((s) => s.auth.role);
  const impersonating = useAppSelector((s) => s.auth.impersonation !== null);
  // See getRoleRedirectPath's doc comment — an impersonating admin gets the owner shell,
  // not the platform admin console, so its nav actually points at studio-scoped pages.
  if (role === Role.Admin && impersonating) return <OwnerLayout />;
  switch (role) {
    case Role.Owner:  return <OwnerLayout />;
    case Role.Artist: return <ArtistLayout />;
    case Role.Client: return <ClientLayout />;
    case Role.Admin: return <AdminLayout />;
    default:          return <Outlet />;
  }
}

export const routes = [
  { path: "/login",           element: <LoginPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  { path: "/reset-password",  element: <ResetPasswordPage /> },
  { path: "/verify-email",    element: <VerifyEmailPage /> },
  { path: "/confirm-change-email", element: <ConfirmChangeEmailPage /> },
  { path: "/register",         element: <RegisterStudioPage /> },
  { path: "/client-register", element: <ClientRegisterPage /> },
  { path: "/map",             element: <StudioMapPage /> },
  { path: "/discover",        element: <DiscoverPage /> },
  { path: "/s/:slug",                 element: <StudioPortfolioPage /> },
  { path: "/artist/:slug",            element: <ArtistPortfolioPage /> },
  { path: "/share/:token",            element: <SharedDesignPage /> },
  { path: "/embed/:studioSlug",       element: <EmbedPage /> },

  // ── Public policy / legal surfaces (PENA-101, PENA-102) ─────────────────────
  // Top-level, outside the authenticated AppRoot tree. Before these existed,
  // CatchAllRedirect silently bounced /privacy and /terms to /discover.
  { path: "/privacy",         element: <PrivacyPolicyPage /> },
  { path: "/terms",           element: <TermsOfServicePage /> },
  { path: "/refund-policy",   element: <RefundPolicyPage /> },
  { path: "/contact",         element: <ContactPage /> },
  { path: "/unsubscribe",     element: <UnsubscribePage /> },

  {
    path: "/",
    element: <AppRoot />,
    children: [
      { index: true, element: <IndexRedirect /> },
      // /book is reachable unauthenticated (guest checkout, Decision #1) — sits outside the
      // blanket auth RoleGuard below, but still gets AppLayout for an authenticated visit
      // (AppLayout itself already falls back to a bare Outlet with no role, matching the
      // guest page's own self-contained PublicPageHeader shell). BookPage internally
      // preserves the pre-existing Client/Admin-only restriction for authenticated users.
      {
        element: <AppLayout />,
        children: [
          { path: "book", element: <ErrorBoundary><BookPage /></ErrorBoundary> },
        ],
      },
      {
        element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
        children: [
          {
            element: <AppLayout />,
            children: [
              // ── Client ──────────────────────────────────────────────────────
              {
                path: "my-studios",
                element: <RoleGuard allowedRoles={[Role.Client]} />,
                children: [{ index: true, element: <ErrorBoundary><MyStudiosPage /></ErrorBoundary> }],
              },

              // ── Artist + Owner ───────────────────────────────────────────────
              {
                path: "schedule",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Admin]} />,
                children: [{ index: true, element: <ErrorBoundary><SchedulePage /></ErrorBoundary> }],
              },

              // ── Owner ───────────────────────────────────────────────────────
              {
                path: "dashboard",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [{ index: true, element: <ErrorBoundary><DashboardPage /></ErrorBoundary> }],
              },

              // ── Admin platform ─────────────────────────────────────────────
              {
                path: "platform",
                element: <RoleGuard allowedRoles={[Role.Admin]} />,
                children: [
                  { index: true,               element: <ErrorBoundary><AdminDashboardPage /></ErrorBoundary> },
                  { path: "traffic",           element: <ErrorBoundary><LiveTrafficPage /></ErrorBoundary> },
                  { path: "studios",           element: <ErrorBoundary><AdminStudioListPage /></ErrorBoundary> },
                  { path: "studios/:studioId", element: <ErrorBoundary><AdminStudioDetailPage /></ErrorBoundary> },
                  {
                    path: "plans",
                    children: [
                      { index: true,        element: <ErrorBoundary><PlanManagementPage /></ErrorBoundary> },
                      { path: "new",        element: <ErrorBoundary><PlanEditPage /></ErrorBoundary> },
                      { path: ":planId/edit", element: <ErrorBoundary><PlanEditPage /></ErrorBoundary> },
                    ],
                  },
                  { path: "subscriptions",     element: <ErrorBoundary><SubscriptionOversightPage /></ErrorBoundary> },
                  { path: "referrals",         element: <ErrorBoundary><PlatformReferralPage /></ErrorBoundary> },
                  { path: "reports",           element: <ErrorBoundary><IndustryReportsPage /></ErrorBoundary> },
                  { path: "feedback",          element: <ErrorBoundary><FeedbackInboxPage /></ErrorBoundary> },
                  { path: "help-insights",     element: <ErrorBoundary><HelpInsightsPage /></ErrorBoundary> },
                  { path: "audit-log",         element: <ErrorBoundary><AuditLogPage /></ErrorBoundary> },
                  { path: "conduct-reports",   element: <ErrorBoundary><ConductReportInboxPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: appointments ────────────────────────────────────────
              {
                path: "appointments",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Admin]} />,
                children: [
                  { path: ":id", element: <ErrorBoundary><AppointmentDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: artists ─────────────────────────────────────────────
              {
                path: "artists",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Admin]} />,
                children: [
                  { index: true, element: <ErrorBoundary><ArtistListPage /></ErrorBoundary> },
                  {
                    path: "new",
                    element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                    children: [{ index: true, element: <ErrorBoundary><CreateArtistPage /></ErrorBoundary> }],
                  },
                  { path: ":id", element: <ErrorBoundary><ArtistDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Client: self-profile ────────────────────────────────────────
              {
                path: "clients/me",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
                children: [{ index: true, element: <ErrorBoundary><MyProfilePage /></ErrorBoundary> }],
              },

              // ── Shared: clients ─────────────────────────────────────────────
              {
                path: "clients",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Admin]} />,
                children: [
                  { index: true,                   element: <ErrorBoundary><ClientListPage /></ErrorBoundary> },
                  { path: "new",                   element: <ErrorBoundary><CreateClientPage /></ErrorBoundary> },
                  { path: ":id",                   element: <ErrorBoundary><ClientDetailPage /></ErrorBoundary> },
                  { path: ":id/tattoos/:tattooId", element: <ErrorBoundary><TattooRecordDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: designs ─────────────────────────────────────────────
              {
                path: "designs",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
                children: [
                  { index: true, element: <ErrorBoundary><DesignListPage /></ErrorBoundary> },
                  {
                    element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Admin]} />,
                    children: [
                      { path: "new",        element: <ErrorBoundary><CreateDesignPage /></ErrorBoundary> },
                      { path: ":id/upload", element: <ErrorBoundary><UploadRevisionPage /></ErrorBoundary> },
                    ],
                  },
                  { path: ":id", element: <ErrorBoundary><DesignDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: deposit rules ───────────────────────────────────────
              {
                path: "deposit-rules",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Admin]} />,
                children: [
                  { index: true, element: <ErrorBoundary><DepositRuleListPage /></ErrorBoundary> },
                  {
                    path: "new",
                    element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                    children: [{ index: true, element: <ErrorBoundary><CreateDepositRulePage /></ErrorBoundary> }],
                  },
                  { path: ":id", element: <ErrorBoundary><DepositRuleDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Owner: intake form builder ───────────────────────────────────
              {
                path: "intake-form-builder",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [{ index: true, element: <ErrorBoundary><IntakeFormBuilderPage /></ErrorBoundary> }],
              },

              // ── Owner: promo codes ──────────────────────────────────────────
              {
                path: "promo-codes",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [
                  { index: true, element: <ErrorBoundary><PromoCodeListPage /></ErrorBoundary> },
                  { path: "new", element: <ErrorBoundary><CreatePromoCodePage /></ErrorBoundary> },
                  { path: ":id", element: <ErrorBoundary><PromoCodeDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: forms ────────────────────────────────────────────────
              {
                path: "forms",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
                children: [
                  {
                    path: "intake/new",
                    element: <RoleGuard allowedRoles={[Role.Client]} />,
                    children: [{ index: true, element: <ErrorBoundary><SubmitIntakeFormPage /></ErrorBoundary> }],
                  },
                  {
                    path: "intake",
                    element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
                    children: [
                      { index: true, element: <ErrorBoundary><IntakeFormListPage /></ErrorBoundary> },
                      { path: ":id", element: <ErrorBoundary><IntakeFormDetailPage /></ErrorBoundary> },
                    ],
                  },
                  {
                    path: "consent/new",
                    element: <RoleGuard allowedRoles={[Role.Client]} />,
                    children: [{ index: true, element: <ErrorBoundary><SignConsentFormPage /></ErrorBoundary> }],
                  },
                  {
                    path: "consent",
                    element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
                    children: [
                      { index: true, element: <ErrorBoundary><ConsentFormListPage /></ErrorBoundary> },
                      { path: ":id", element: <ErrorBoundary><ConsentFormDetailPage /></ErrorBoundary> },
                    ],
                  },
                ],
              },

              // ── Shared: notifications ───────────────────────────────────────
              {
                path: "notifications",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Admin]} />,
                children: [
                  { index: true, element: <ErrorBoundary><NotificationLogListPage /></ErrorBoundary> },
                ],
              },

              // ── Owner: billing ──────────────────────────────────────────────
              {
                path: "billing",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [
                  { index: true,       element: <ErrorBoundary><BillingPage /></ErrorBoundary> },
                  { path: "subscribe", element: <ErrorBoundary><SubscribePage /></ErrorBoundary> },
                ],
              },

              // ── Owner: studio profile ───────────────────────────────────────
              {
                path: "studios",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [
                  { path: "me", element: <ErrorBoundary><StudioProfilePage /></ErrorBoundary> },
                ],
              },

              // ── Owner: reports ───────────────────────────────────────────────
              {
                path: "reports",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [
                  { index: true, element: <ErrorBoundary><ReportsPage /></ErrorBoundary> },
                ],
              },

              // ── Owner: marketing campaigns ──────────────────────────────────
              {
                path: "campaigns",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [
                  { index: true, element: <ErrorBoundary><CampaignsPage /></ErrorBoundary> },
                ],
              },

              // ── Artist (+ owner-as-artist): my earnings ─────────────────────
              {
                path: "earnings",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner]} />,
                children: [
                  { index: true, element: <ErrorBoundary><MyEarningsPage /></ErrorBoundary> },
                ],
              },

              // ── Owner + Artist: conduct reports ─────────────────────────────
              {
                path: "conduct-reports",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner]} />,
                children: [
                  { index: true, element: <ErrorBoundary><ConductReportsPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: in-app messaging (client, artist, owner — not admin, see
              // messaging Decision 1) ─────────────────────────────────────────
              {
                path: "messages",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner]} />,
                children: [{ index: true, element: <ErrorBoundary><MessagesInboxPage /></ErrorBoundary> }],
              },

              // ── Owner: payments ─────────────────────────────────────────────
              {
                path: "payments",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Admin]} />,
                children: [
                  { index: true,            element: <ErrorBoundary><PaymentListPage /></ErrorBoundary> },
                  { path: "new",            element: <ErrorBoundary><CreatePaymentIntentPage /></ErrorBoundary> },
                  { path: ":appointmentId", element: <ErrorBoundary><PaymentDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Client: deposit checkout ────────────────────────────────────
              {
                path: "pay/:paymentId",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Owner, Role.Artist, Role.Admin]} />,
                children: [{ index: true, element: <ErrorBoundary><DepositCheckoutPage /></ErrorBoundary> }],
              },

              // ── Auth: account settings ───────────────────────────────────────
              {
                path: "account/change-password",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
                children: [{ index: true, element: <ErrorBoundary><ChangePasswordPage /></ErrorBoundary> }],
              },
              {
                path: "account/change-email",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Admin]} />,
                children: [{ index: true, element: <ErrorBoundary><RequestChangeEmailPage /></ErrorBoundary> }],
              },
            ],
          },
        ],
      },
    ],
  },
  { path: "*", element: <CatchAllRedirect /> },
];

export const router = createBrowserRouter(routes);
