/** DEC03: preparation requirements a service turns on for the client's checklist. All default to false. */
export interface ServiceRequirementFlags {
  requiresConsentForm:    boolean;
  requiresIntakeForm:     boolean;
  requiresReferenceImage: boolean;
  requiresDepositPaid:    boolean;
}

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
  requiresConsentForm?:    boolean;
  requiresIntakeForm?:     boolean;
  requiresReferenceImage?: boolean;
  requiresDepositPaid?:    boolean;
}

export interface CreateServiceRequest extends Partial<ServiceRequirementFlags> {
  name:            string;
  description:     string | null;
  durationMinutes: number;
  price:           number | null;
  depositAmount:   number | null;
  isActive:        boolean;
}

export interface UpdateServiceRequest extends Partial<ServiceRequirementFlags> {
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
