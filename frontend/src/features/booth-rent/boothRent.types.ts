export const RentFrequency = {
  Weekly:  "Weekly",
  Monthly: "Monthly",
} as const;
export type RentFrequency = (typeof RentFrequency)[keyof typeof RentFrequency];

export interface BoothRentScheduleResponse {
  id:              string;
  studioId:        string;
  artistId:        string;
  artistName:      string | null;
  amountFixed:     number;
  frequency:       RentFrequency;
  nextChargeDate:  string;
  isActive:        boolean;
  createdAt:       string;
  updatedAt:       string;
}

export interface CreateBoothRentScheduleRequest {
  artistId:       string;
  amountFixed:    number;
  frequency:      RentFrequency;
  nextChargeDate: string;
  isActive:       boolean;
}

export interface UpdateBoothRentScheduleRequest {
  amountFixed:    number;
  frequency:      RentFrequency;
  nextChargeDate: string;
  isActive:       boolean;
}

export interface BoothRentChargeResponse {
  id:                   string;
  studioId:             string;
  artistId:             string;
  artistName:           string | null;
  boothRentScheduleId:  string;
  amount:               number;
  chargedDate:          string;
  isSettled:            boolean;
  settledAt:            string | null;
  settledNote:          string | null;
  createdAt:            string;
}

export interface MarkBoothRentChargeSettledRequest {
  settledNote: string | null;
}
