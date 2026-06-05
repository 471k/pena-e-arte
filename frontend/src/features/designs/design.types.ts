export interface DesignResponse {
  id:          string;
  studioId:    string;
  clientId:    string;
  artistId:    string;
  title:       string;
  description: string | null;
  createdAt:   string;
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
