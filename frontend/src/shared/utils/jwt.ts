import { jwtDecode } from "jwt-decode";
import { Role, type User, type AuthPayload } from "@/shared/types/roles";

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

interface JwtClaims {
  sub: string;
  email: string;
  exp?: number;
  tenant_id?: string;
  [ROLE_CLAIM]?: string;
}

export function decodeToken(token: string): AuthPayload & { exp?: number } {
  const claims = jwtDecode<JwtClaims>(token);

  const user: User = {
    id: claims.sub,
    email: claims.email,
  };

  const rawRole = claims[ROLE_CLAIM] ?? "";
  const role = Object.values(Role).includes(rawRole as Role)
    ? (rawRole as Role)
    : Role.Client;

  return {
    user,
    token,
    tenantId: claims.tenant_id ?? null,
    role,
    exp: claims.exp,
  };
}
