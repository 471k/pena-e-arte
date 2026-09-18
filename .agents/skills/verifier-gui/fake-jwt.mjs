/**
 * Build an unsigned JWT that jwt-decode accepts on the frontend.
 * The frontend (authSlice.ts) reads localStorage["auth_token"] on startup
 * and calls decodeToken() which uses jwt-decode — no signature check.
 *
 * Usage:
 *   import { makeFakeJwt, ROLE_CLAIM } from "./fake-jwt.mjs";
 *   const token = makeFakeJwt({ role: "artist", tenantId: "..." });
 */

export const ROLE_CLAIM =
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

/**
 * @param {{ role: string, tenantId?: string, userId?: string, email?: string }} opts
 * @returns {string}  header.payload.fakesig
 */
export function makeFakeJwt({
  role     = "artist",
  tenantId = "00000000-0000-0000-0000-000000000002",
  userId   = "00000000-0000-0000-0000-000000000001",
  email    = `${role}@verify.test`,
} = {}) {
  const header  = Buffer.from(JSON.stringify({ alg: "HS256", typ: "JWT" })).toString("base64url");
  const payload = Buffer.from(JSON.stringify({
    sub:         userId,
    email,
    tenant_id:   tenantId,
    [ROLE_CLAIM]: role,
    exp:         9999999999,
  })).toString("base64url");
  return `${header}.${payload}.fakesig`;
}
