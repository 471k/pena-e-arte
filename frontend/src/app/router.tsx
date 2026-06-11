import { useEffect } from "react";
import { Navigate, Outlet, createBrowserRouter, useNavigate } from "react-router-dom";
import { LoginPage, ForgotPasswordPage, ResetPasswordPage } from "@/features/auth";
import { RegisterStudioPage, ConnectStudioPage, ConnectReturnPage, ConnectRefreshPage, StudioProfilePage } from "@/features/studios";
import { BillingPage, SubscribePage } from "@/features/billing";
import { DashboardPage } from "@/features/dashboard";
import { StudioMapPage } from "@/features/map";
import { SchedulePage, BookPage, AppointmentDetailPage } from "@/features/appointments";
import { ArtistListPage, ArtistDetailPage, CreateArtistPage } from "@/features/artists";
import { ClientListPage, CreateClientPage, ClientDetailPage, TattooRecordDetailPage } from "@/features/clients";
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
import { NotificationLogListPage } from "@/features/notifications";
import { PaymentListPage, PaymentDetailPage, CreatePaymentIntentPage } from "@/features/payments";
import {
  IssuerDashboardPage,
  IssuerStudioListPage,
  PlanManagementPage,
  SubscriptionOversightPage,
  PlatformReferralPage,
  IndustryReportsPage,
} from "@/features/platform";
import { StudioPortfolioPage, ArtistPortfolioPage, SharedDesignPage } from "@/features/public";
import { ClientLayout } from "@/layouts/ClientLayout";
import { ArtistLayout } from "@/layouts/ArtistLayout";
import { OwnerLayout } from "@/layouts/OwnerLayout";
import { IssuerLayout } from "@/layouts/IssuerLayout";
import { Role } from "@/shared/types/roles";
import { clearSessionExpired } from "@/features/ui/uiSlice";
import { useAppDispatch, useAppSelector } from "./hooks";

function RoleGuard({ allowedRoles }: { allowedRoles: Role[] }) {
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
  if (!role) return <Navigate to="/login" replace />;
  return <Navigate to={getRoleRedirectPath(role)} replace />;
}

function AppRoot() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const sessionExpired = useAppSelector((s) => s.ui.sessionExpired);

  useEffect(() => {
    if (sessionExpired) {
      dispatch(clearSessionExpired());
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
  { path: "/register",        element: <RegisterStudioPage /> },
  { path: "/map",             element: <StudioMapPage /> },
  { path: "/s/:slug",         element: <StudioPortfolioPage /> },
  { path: "/artist/:slug",    element: <ArtistPortfolioPage /> },
  { path: "/share/:token",    element: <SharedDesignPage /> },
  {
    path: "/",
    element: <AppRoot />,
    children: [
      {
        element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          { index: true, element: <IndexRedirect /> },
          {
            element: <AppLayout />,
            children: [
              // ── Client ──────────────────────────────────────────────────────
              {
                path: "book",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Issuer]} />,
                children: [{ index: true, element: <BookPage /> }],
              },

              // ── Artist ──────────────────────────────────────────────────────
              {
                path: "schedule",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Issuer]} />,
                children: [{ index: true, element: <SchedulePage /> }],
              },

              // ── Owner ───────────────────────────────────────────────────────
              {
                path: "dashboard",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [{ index: true, element: <DashboardPage /> }],
              },

              // ── Issuer platform ─────────────────────────────────────────────
              {
                path: "platform",
                element: <RoleGuard allowedRoles={[Role.Issuer]} />,
                children: [
                  { index: true,             element: <IssuerDashboardPage /> },
                  { path: "studios",         element: <IssuerStudioListPage /> },
                  { path: "plans",           element: <PlanManagementPage /> },
                  { path: "subscriptions",   element: <SubscriptionOversightPage /> },
                  { path: "referrals",       element: <PlatformReferralPage /> },
                  { path: "reports",         element: <IndustryReportsPage /> },
                ],
              },

              // ── Shared: appointments ────────────────────────────────────────
              {
                path: "appointments",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { path: ":id", element: <AppointmentDetailPage /> },
                ],
              },

              // ── Shared: artists ─────────────────────────────────────────────
              {
                path: "artists",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <ArtistListPage /> },
                  {
                    path: "new",
                    element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                    children: [{ index: true, element: <CreateArtistPage /> }],
                  },
                  { path: ":id", element: <ArtistDetailPage /> },
                ],
              },

              // ── Shared: clients ─────────────────────────────────────────────
              {
                path: "clients",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true,                   element: <ClientListPage /> },
                  { path: "new",                   element: <CreateClientPage /> },
                  { path: ":id",                   element: <ClientDetailPage /> },
                  { path: ":id/tattoos/:tattooId", element: <TattooRecordDetailPage /> },
                ],
              },

              // ── Shared: designs ─────────────────────────────────────────────
              {
                path: "designs",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  {
                    element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                    children: [
                      { index: true,        element: <DesignListPage /> },
                      { path: "new",        element: <CreateDesignPage /> },
                      { path: ":id/upload", element: <UploadRevisionPage /> },
                    ],
                  },
                  { path: ":id", element: <DesignDetailPage /> },
                ],
              },

              // ── Shared: deposit rules ───────────────────────────────────────
              {
                path: "deposit-rules",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <DepositRuleListPage /> },
                  {
                    path: "new",
                    element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                    children: [{ index: true, element: <CreateDepositRulePage /> }],
                  },
                  { path: ":id", element: <DepositRuleDetailPage /> },
                ],
              },

              // ── Shared: forms ────────────────────────────────────────────────
              {
                path: "forms",
                element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { path: "intake/new", element: <SubmitIntakeFormPage /> },
                  {
                    path: "intake",
                    element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                    children: [
                      { index: true, element: <IntakeFormListPage /> },
                      { path: ":id", element: <IntakeFormDetailPage /> },
                    ],
                  },
                  { path: "consent/new", element: <SignConsentFormPage /> },
                  {
                    path: "consent",
                    element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                    children: [
                      { index: true, element: <ConsentFormListPage /> },
                      { path: ":id", element: <ConsentFormDetailPage /> },
                    ],
                  },
                ],
              },

              // ── Shared: notifications ───────────────────────────────────────
              {
                path: "notifications",
                element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true, element: <NotificationLogListPage /> },
                ],
              },

              // ── Owner: billing ──────────────────────────────────────────────
              {
                path: "billing",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true,       element: <BillingPage /> },
                  { path: "subscribe", element: <SubscribePage /> },
                ],
              },

              // ── Owner: studio profile ───────────────────────────────────────
              {
                path: "studios",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { path: "me", element: <StudioProfilePage /> },
                ],
              },

              // ── Owner: studio connect (Stripe OAuth — no sub-nav needed) ────
              {
                path: "studio",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { path: "connect",         element: <ConnectStudioPage /> },
                  { path: "connect/return",  element: <ConnectReturnPage /> },
                  { path: "connect/refresh", element: <ConnectRefreshPage /> },
                ],
              },

              // ── Owner: payments ─────────────────────────────────────────────
              {
                path: "payments",
                element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
                children: [
                  { index: true,            element: <PaymentListPage /> },
                  { path: "new",            element: <CreatePaymentIntentPage /> },
                  { path: ":appointmentId", element: <PaymentDetailPage /> },
                ],
              },
            ],
          },
        ],
      },
    ],
  },
  { path: "*", element: <Navigate to="/login" replace /> },
]);
