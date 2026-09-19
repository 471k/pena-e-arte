export interface ServiceResponse {
  id:              string;
  studioId:        string;
  name:            string;
  description:     string | null;
  durationMinutes: number;
  price:           number | null;
  depositAmount:   number | null;
  isActive:        boolean;
  createdAt:       string;
  updatedAt:       string;
}

export interface CreateServiceRequest {
  name:            string;
  description:     string | null;
  durationMinutes: number;
  price:           number | null;
  depositAmount:   number | null;
  isActive:        boolean;
}

export interface UpdateServiceRequest {
  name:            string;
  description:     string | null;
  durationMinutes: number;
  price:           number | null;
  depositAmount:   number | null;
  isActive:        boolean;
}

export interface PublicServiceResponse {
  id:              string;
  name:            string;
  description:     string | null;
  durationMinutes: number;
  price:           number | null;
  depositAmount:   number | null;
}
