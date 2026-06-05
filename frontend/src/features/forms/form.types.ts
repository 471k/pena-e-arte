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
}

export interface SignConsentFormRequest {
  clientId:      string;
  appointmentId: string;
  signatureData: string;
  fileUrl:       string | null;
}

export interface GetConsentFormsParams {
  clientId?:      string;
  appointmentId?: string;
}
