import { Navigate, Outlet, createBrowserRouter } from "react-router-dom";
import { LoginPage } from "@/features/auth/components/LoginPage";
import { RegisterStudioPage } from "@/features/studios";
import { StudioMapPage } from "@/features/map";
import { SchedulePage, BookPage } from "@/features/appointments";
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
import { Role } from "@/shared/types/roles";
import { useAppSelector } from "./hooks";

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

export const router = createBrowserRouter([
  { path: "/login",    element: <LoginPage /> },
  { path: "/register", element: <RegisterStudioPage /> },
  { path: "/map",      element: <StudioMapPage /> },
  {
    path: "/",
    element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
    children: [
      { path: "book",      element: <BookPage /> },
      { path: "schedule",  element: <SchedulePage /> },
      { path: "dashboard", element: <div>Owner layout (coming soon)</div> },
      { path: "platform",  element: <div>Issuer layout (coming soon)</div> },
      {
        path: "artists",
        element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          { index: true,   element: <ArtistListPage /> },
          {
            path: "new",
            element: <RoleGuard allowedRoles={[Role.Owner, Role.Issuer]} />,
            children: [{ index: true, element: <CreateArtistPage /> }],
          },
          { path: ":id",   element: <ArtistDetailPage /> },
        ],
      },
      {
        path: "clients",
        element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          { index: true,                          element: <ClientListPage /> },
          { path: "new",                          element: <CreateClientPage /> },
          { path: ":id",                          element: <ClientDetailPage /> },
          { path: ":id/tattoos/:tattooId",        element: <TattooRecordDetailPage /> },
        ],
      },
      {
        path: "designs",
        element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          // artist-only: list, create, upload
          {
            element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
            children: [
              { index: true,        element: <DesignListPage /> },
              { path: "new",        element: <CreateDesignPage /> },
              { path: ":id/upload", element: <UploadRevisionPage /> },
            ],
          },
          // clients and above: view detail + review revisions
          { path: ":id", element: <DesignDetailPage /> },
        ],
      },
      {
        path: "forms",
        element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
        children: [
          // submit intake form: ClientAndAbove (all roles)
          { path: "intake/new", element: <SubmitIntakeFormPage /> },
          // intake list + detail: ArtistAndAbove
          {
            path: "intake",
            element: <RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]} />,
            children: [
              { index: true, element: <IntakeFormListPage /> },
              { path: ":id", element: <IntakeFormDetailPage /> },
            ],
          },
          // sign consent form: ClientAndAbove (all roles)
          { path: "consent/new", element: <SignConsentFormPage /> },
          // consent list + detail: ArtistAndAbove
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
    ],
  },
  { path: "*", element: <Navigate to="/login" replace /> },
]);
