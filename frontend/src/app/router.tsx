import { useEffect } from "react";
import { Navigate, Outlet, createBrowserRouter, useNavigate } from "react-router-dom";
import { LoginPage, ForgotPasswordPage, ResetPasswordPage, ChangePasswordPage, VerifyEmailPage, ClientRegisterPage, MyStudiosPage } from "@/features/auth";
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
  IntakeFormDetailPage,
  SignConsentFormPage,
  ConsentFormListPage,
  ConsentFormDetailPage,
} from "@/features/forms";
import { DepositRuleListPage, DepositRuleDetailPage, CreateDepositRulePage } from "@/features/deposit-rules";
import { ReportsPage } from "@/features/reports";
import { NotificationLogListPage } from "@/features/notifications";
import { PaymentListPage, PaymentDetailPage, CreatePaymentIntentPage, DepositCheckoutPage } from "@/features/payments";
import {
  IssuerDashboardPage,
  IssuerStudioListPage,
  IssuerStudioDetailPage,
  PlanManagementPage,
  PlanEditPage,
  SubscriptionOversightPage,
  PlatformReferralPage,
  IndustryReportsPage,
  HelpInsightsPage,
} from "@/features/platform";
import { FeedbackInboxPage } from "@/features/feedback";
import { StudioPortfolioPage, ArtistPortfolioPage, SharedDesignPage, EmbedPage, DiscoverPage } from "@/features/public";
import { ErrorBoundary } from "@/shared/components/ErrorBoundary";
import { ClientLayout } from "@/layouts/ClientLayout";
import { ArtistLayout } from "@/layouts/ArtistLayout";
import { OwnerLayout } from "@/layouts/OwnerLayout";
import { IssuerLayout } from "@/layouts/IssuerLayout";
import { Role } from "@/shared/types/roles";
import { logout } from "@/features/auth/authSlice";
import { clearSessionExpired } from "@/features/ui/uiSlice";
import { useAppDispatch, useAppSelector } from "./hooks";

export function RoleGuard({ allowedRoles }: { allowedRoles: Role[] }) {
  const role = useAppSelector((s) => s.auth.role);

  if (!role) return <Navigate to="/login" replace />;
  if (!allowedRoles.includes(role)) return <Navigate to={getRoleRedirectPath(role)} replace />;

  return <Outlet />;
}

export function getRoleRedirectPath(role: Role): string {
  switch (role) {
    case Role.Client: return "/book";
    case Role.Artist: return "/schedule";
    case Role.Owner: return "/dashboard";
    case Role.Issuer: return "/platform";
  }
}

function IndexRedirect() {
  const role = useAppSelector((s) => s.auth.role);
  if (!role) return <Navigate to="/discover" replace />;
  return <Navigate to={getRoleRedirectPath(role)} replace />;
}

function CatchAllRedirect() {
  const role = useAppSelector((s) => s.auth.role);
  if (!role) return <Navigate to="/discover" replace />;
  return <Navigate to={getRoleRedirectPath(role)} replace />;
}

export function AppRoot() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const sessionExpired = useAppSelector((s) => s.ui.sessionExpired);

  useEffect(() => {
    if (sessionExpired) {
      dispatch(clearSessionExpired());
      dispatch(logout());
      navigate("/login?reason=session_expired", { replace: true });
    }
  }, [sessionExpired, dispatch, navigate]);

  return <Outlet />;
}

function AppLayout() {
  const role = useAppSelector((s) => s.auth.role);
  switch (role) {
    case Role.Owner:  return <OwnerLayout />;
    case Role.Artist: return <ArtistLayout />;
    case Role.Client: return <ClientLayout />;
    case Role.Issuer: return <IssuerLayout />;
    default:          return <Outlet />;
  }
}

export const router = createBrowserRouter([
  { path: "/login",           element: <LoginPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  { path: "/reset-password",  element: <ResetPasswordPage /> },
  { path: "/verify-email",    element: <VerifyEmailPage /> },
  { path: "/register",         element: <RegisterStudioPage /> },
  { path: "/client-register", element: <ClientRegisterPage /> },
  { path: "/map",             element: <StudioMapPage /> },
  { path: "/discover",        element: <DiscoverPage /> },
  { path: "/s/:slug",                 element: <StudioPortfolioPage /> },
  { path: "/artist/:slug",            element: <ArtistPortfolioPage /> },
  { path: "/share/:token",            element: <SharedDesignPage /> },
  { path: "/embed/:studioSlug",       element: <EmbedPage /> },
  {
    path: "/",
    element: <AppRoot />,
    children: [
      { index: true, element: <IndexRedirect /> },
      {
        element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          {
            element: <AppLayout />,
            children: [
              // ── Client ──────────────────────────────────────────────────────
              {
                path: "book",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Issuer]} />,
                children: [{ index: true, element: <ErrorBoundary><BookPage /></ErrorBoundary> }],
              },
              {
                path: "my-studios",
                element: <RoleGuard allowedRoles={[Role.Client]} />,
                children: [{ index: true, element: <ErrorBoundary><MyStudiosPage /></ErrorBoundary> }],
              },

              // ── Artist + Owner ───────────────────────────────────────────────
              {
                path: "schedule",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [{ index: true, element: <ErrorBoundary><SchedulePage /></ErrorBoundary> }],
              },

              // ── Owner ───────────────────────────────────────────────────────
              {
                path: "dashboard",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [{ index: true, element: <ErrorBoundary><DashboardPage /></ErrorBoundary> }],
              },

              // ── Issuer platform ─────────────────────────────────────────────
              {
                path: "platform",
                element: <RoleGuard allowedRoles={[Role.Issuer]} />,
                children: [
                  { index: true,               element: <ErrorBoundary><IssuerDashboardPage /></ErrorBoundary> },
                  { path: "studios",           element: <ErrorBoundary><IssuerStudioListPage /></ErrorBoundary> },
                  { path: "studios/:studioId", element: <ErrorBoundary><IssuerStudioDetailPage /></ErrorBoundary> },
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
                ],
              },

              // ── Shared: appointments ────────────────────────────────────────
              {
                path: "appointments",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { path: ":id", element: <ErrorBoundary><AppointmentDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: artists ─────────────────────────────────────────────
              {
                path: "artists",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <ErrorBoundary><ArtistListPage /></ErrorBoundary> },
                  {
                    path: "new",
                    element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                    children: [{ index: true, element: <ErrorBoundary><CreateArtistPage /></ErrorBoundary> }],
                  },
                  { path: ":id", element: <ErrorBoundary><ArtistDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Client: self-profile ────────────────────────────────────────
              {
                path: "clients/me",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [{ index: true, element: <ErrorBoundary><MyProfilePage /></ErrorBoundary> }],
              },

              // ── Shared: clients ─────────────────────────────────────────────
              {
                path: "clients",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
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
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <ErrorBoundary><DesignListPage /></ErrorBoundary> },
                  {
                    element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
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
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <ErrorBoundary><DepositRuleListPage /></ErrorBoundary> },
                  {
                    path: "new",
                    element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                    children: [{ index: true, element: <ErrorBoundary><CreateDepositRulePage /></ErrorBoundary> }],
                  },
                  { path: ":id", element: <ErrorBoundary><DepositRuleDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Shared: forms ────────────────────────────────────────────────
              {
                path: "forms",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  {
                    path: "intake/new",
                    element: <RoleGuard allowedRoles={[Role.Client]} />,
                    children: [{ index: true, element: <ErrorBoundary><SubmitIntakeFormPage /></ErrorBoundary> }],
                  },
                  {
                    path: "intake",
                    element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
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
                    element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
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
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <ErrorBoundary><NotificationLogListPage /></ErrorBoundary> },
                ],
              },

              // ── Owner: billing ──────────────────────────────────────────────
              {
                path: "billing",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true,       element: <ErrorBoundary><BillingPage /></ErrorBoundary> },
                  { path: "subscribe", element: <ErrorBoundary><SubscribePage /></ErrorBoundary> },
                ],
              },

              // ── Owner: studio profile ───────────────────────────────────────
              {
                path: "studios",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { path: "me", element: <ErrorBoundary><StudioProfilePage /></ErrorBoundary> },
                ],
              },

              // ── Owner: reports ───────────────────────────────────────────────
              {
                path: "reports",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <ErrorBoundary><ReportsPage /></ErrorBoundary> },
                ],
              },

              // ── Owner: payments ─────────────────────────────────────────────
              {
                path: "payments",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true,            element: <ErrorBoundary><PaymentListPage /></ErrorBoundary> },
                  { path: "new",            element: <ErrorBoundary><CreatePaymentIntentPage /></ErrorBoundary> },
                  { path: ":appointmentId", element: <ErrorBoundary><PaymentDetailPage /></ErrorBoundary> },
                ],
              },

              // ── Client: deposit checkout ────────────────────────────────────
              {
                path: "pay/:paymentId",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Owner, Role.Artist, Role.Issuer]} />,
                children: [{ index: true, element: <ErrorBoundary><DepositCheckoutPage /></ErrorBoundary> }],
              },

              // ── Auth: account settings ───────────────────────────────────────
              {
                path: "account/change-password",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [{ index: true, element: <ErrorBoundary><ChangePasswordPage /></ErrorBoundary> }],
              },
            ],
          },
        ],
      },
    ],
  },
  { path: "*", element: <CatchAllRedirect /> },
]);
