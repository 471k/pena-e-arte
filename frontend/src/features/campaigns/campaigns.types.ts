export type CampaignAudience = "AllClients" | "ClientsWithNoRecentVisit" | "Custom";
export type CampaignStatus = "Draft" | "Sending" | "Sent" | "Failed";

export interface CampaignResponse {
  id: string;
  subject: string;
  bodyHtml: string;
  audience: CampaignAudience;
  status: CampaignStatus;
  noRecentVisitDays: number | null;
  customClientIds: string[];
  sentAt: string | null;
  recipientCount: number;
  deliveredCount: number;
  createdAt: string;
}

export interface CreateCampaignRequest {
  subject: string;
  bodyHtml: string;
  audience: CampaignAudience;
  noRecentVisitDays?: number;
  customClientIds?: string[];
}
