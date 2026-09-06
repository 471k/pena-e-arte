export const Role = {
  Client: "client",
  Artist: "artist",
  Owner: "owner",
  Admin: "admin",
} as const;

export type Role = (typeof Role)[keyof typeof Role];

export interface User {
  id: string;
  email: string;
  name?: string;
  emailVerified?: boolean;
}

export interface AuthPayload {
  user:         User;
  token:        string;
  refreshToken?: string | null;
  tenantId:     string | null;
  role:         Role;
}
