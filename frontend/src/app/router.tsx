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
import { IssuerStudioListPage, PlanManagementPage } from "@/features/platform";
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
    case Role.Issuer: return "/platform/studios";
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

export const router = createBrowserRouter([
  { path: "/login",           element: <LoginPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  { path: "/reset-password",  element: <ResetPasswordPage /> },
  { path: "/register",        element: <RegisterStudioPage /> },
  { path: "/map",             element: <StudioMapPage /> },
  {
    path: "/",
    element: <AppRoot />,
    children: [
      {
        element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
      { index: true, element: <IndexRedirect /> },

      // Client routes
      {
        path: "book",
        element: <RoleGuard allowedRoles={[Role.Client, Role.Issuer]} />,
        children: [
          {
            element: <ClientLayout />,
            children: [{ index: true, element: <BookPage /> }],
          },
        ],
      },

      // Artist routes
      {
        path: "schedule",
        element: <RoleGuard allowedRoles={[Role.Artist, Role.Issuer]} />,
        children: [
          {
            element: <ArtistLayout />,
            children: [{ index: true, element: <SchedulePage /> }],
          },
        ],
      },

      // Owner routes
      {
        path: "dashboard",
        element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
        children: [
          {
            element: <OwnerLayout />,
            children: [{ index: true, element: <DashboardPage /> }],
          },
        ],
      },

      // Appointment detail
      {
        path: "appointments",
        element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          { path: ":id", element: <AppointmentDetailPage /> },
        ],
      },

      // Issuer platform
      {
        path: "platform",
        element: <RoleGuard allowedRoles={[Role.Issuer]} />,
        children: [
          {
            element: <IssuerLayout />,
            children: [
              { index: true,          element: <Navigate to="/platform/studios" replace /> },
              { path: "studios",      element: <IssuerStudioListPage /> },
              { path: "plans",        element: <PlanManagementPage /> },
            ],
          },
        ],
      },

      {
        path: "billing",
        element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
        children: [
          { index: true,       element: <BillingPage /> },
          { path: "subscribe", element: <SubscribePage /> },
        ],
      },
      {
        path: "studios",
        element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
        children: [
          {
            element: <OwnerLayout />,
            children: [
              { path: "me", element: <StudioProfilePage /> },
            ],
          },
        ],
      },
      {
        path: "studio",
        element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
        children: [
          { path: "connect",         element: <ConnectStudioPage /> },
          { path: "connect/return",  element: <ConnectReturnPage /> },
          { path: "connect/refresh", element: <ConnectRefreshPage /> },
        ],
      },
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
      {
        path: "notifications",
        element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          { index: true, element: <NotificationLogListPage /> },
        ],
      },
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
  { path: "*", element: <Navigate to="/login" replace /> },
]);
