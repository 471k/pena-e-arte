import { Navigate, Outlet, createBrowserRouter } from "react-router-dom";
import { LoginPage } from "@/features/auth/components/LoginPage";
import { RegisterStudioPage } from "@/features/studios";
import { Role } from "@/shared/types/roles";
import { useAppSelector } from "./hooks";

function RoleGuard({ allowedRoles }: { allowedRoles: Role[] }) {
  const role = useAppSelector((s) => s.auth.role);

  if (!role) return <Navigate to="/login" replace />;
  if (!allowedRoles.includes(role)) return <Navigate to="/login" replace />;

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
  {
    path: "/",
    element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner, Role.Issuer]} />,
    children: [
      { path: "book",      element: <div>Client layout (coming soon)</div> },
      { path: "schedule",  element: <div>Artist layout (coming soon)</div> },
      { path: "dashboard", element: <div>Owner layout (coming soon)</div> },
      { path: "platform",  element: <div>Issuer layout (coming soon)</div> },
    ],
  },
  { path: "*", element: <Navigate to="/login" replace /> },
]);
