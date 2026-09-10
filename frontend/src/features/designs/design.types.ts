export type DesignStatus = "Draft" | "InReview" | "Approved" | "ChangesRequested";

export interface DesignResponse {
  id:            string;
  studioId:      string;
  clientId:      string | null;
  artistId:      string;
  title:         string;
  description:   string | null;
  createdAt:     string;
  status:        DesignStatus;
  isCatalogItem: boolean;
  price:         number | null;
}

export interface MarkDesignAsCatalogItemRequest {
  isCatalogItem: boolean;
  price:         number | null;
}

export interface DesignCatalogItemResponse {
  id:         string;
  title:      string;
  description: string | null;
  price:      number | null;
  artistId:   string;
  artistName: string;
  imageUrl:   string | null;
}

export interface GetDesignsParams {
  clientId?: string;
  artistId?: string;
}

export interface CreateDesignRequest {
  clientId:    string;
  artistId:    string;
  title:       string;
  description: string | null;
}

export interface DesignRevisionResponse {
  id:             string;
  designId:       string;
  versionNumber:  number;
  fileUrl:        string;
  notes:          string | null;
  uploadedAt:     string;
  approvalStatus: string | null;
  approvalNotes:  string | null;
}

export interface UploadRevisionRequest {
  designId: string;
  fileUrl:  string;
  notes:    string | null;
}

export interface ReviewRevisionRequest {
  revisionId: string;
  approved:   boolean;
  notes:      string | null;
}

export interface DesignShareTokenResponse {
  id:        string;
  token:     string;
  shareUrl:  string;
  expiresAt: string;
}
