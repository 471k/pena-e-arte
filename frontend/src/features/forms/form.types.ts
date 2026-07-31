export interface IntakeFormResponse {
  id:            string;
  studioId:      string;
  clientId:      string;
  appointmentId: string | null;
  formData:      string;
  fileUrl:       string | null;
  submittedAt:   string | null;
  createdAt:     string;
}

export interface SubmitIntakeFormRequest {
  clientId:      string;
  appointmentId: string | null;
  formData:      string;
  fileUrl:       string | null;
}

export interface GetIntakeFormsParams {
  clientId?:      string;
  appointmentId?: string;
}

export interface ConsentFormResponse {
  id:            string;
  studioId:      string;
  clientId:      string;
  appointmentId: string;
  fileUrl:       string | null;
  signatureData: string | null;
  signedAt:      string | null;
  createdAt:     string;
  clientName:    string;
}

export interface ConsentFormDetailResponse {
  id:              string;
  studioId:        string;
  clientId:        string;
  appointmentId:   string;
  fileUrl:         string | null;
  signatureData:   string | null;
  signedAt:        string | null;
  createdAt:       string;
  // Resolved by the detail endpoint — never a raw UUID
  clientName:      string;
  appointmentDate: string;
  artistName:      string | null;
  artistId:        string | null;
  // The exact consent text the client agreed to at signing time (immutable snapshot).
  // Null for forms signed before consent versioning existed.
  consentTextSnapshot: string | null;
}

export interface ConsentTemplateResponse {
  id:       string | null;
  kind:     string;
  version:  string;
  bodyText: string;
}

export interface SignConsentFormRequest {
  clientId:      string;
  appointmentId: string;
  signatureData: string;
}

export interface GetConsentFormsParams {
  clientId?:      string;
  appointmentId?: string;
}
