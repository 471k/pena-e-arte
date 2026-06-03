export const Role = {
  Client: "client",
  Artist: "artist",
  Owner: "owner",
  Issuer: "issuer",
} as const;

export type Role = (typeof Role)[keyof typeof Role];

export interface User {
  id: string;
  email: string;
}

export interface AuthPayload {
  user: User;
  token: string;
  tenantId: string | null;
  role: Role;
}
