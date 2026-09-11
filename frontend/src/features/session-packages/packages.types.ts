export interface PackageResponse {
  id:           string;
  studioId:     string;
  name:         string;
  sessionCount: number;
  price:        number;
  isActive:     boolean;
  createdAt:    string;
}

export interface CreatePackageRequest {
  name:         string;
  sessionCount: number;
  price:        number;
  isActive:     boolean;
}

export interface UpdatePackageRequest {
  name:         string;
  sessionCount: number;
  price:        number;
  isActive:     boolean;
}

export interface PackagePurchaseResponse {
  id:                string;
  studioId:          string;
  packageId:         string;
  packageName:       string | null;
  clientId:          string;
  sessionsRemaining: number;
  createdAt:         string;
}

export interface PurchasePackageRequest {
  packageId: string;
}

export interface PurchasePackageResponse {
  packagePurchaseId: string;
  clientToken:      string | null;
}
