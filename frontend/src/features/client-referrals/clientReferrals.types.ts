export interface ClientReferralCodeResponse {
  id: string;
  code: string;
  shareUrl: string;
  rewardPercent: number;
  redemptionCount: number;
}

export interface ClientReferralRewardResponse {
  id: string;
  rewardPercent: number;
  isRedeemed: boolean;
  createdAt: string;
}
