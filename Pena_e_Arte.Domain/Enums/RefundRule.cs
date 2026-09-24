namespace Pena_e_Arte.Domain.Enums;

/// <summary>How a SubscriptionRefund's amount was decided. YearlyFormula is the default,
/// self-service path (YearlyRefundCalculator); AdminFull/AdminNone are an admin override that
/// requires a reason.</summary>
public enum RefundRule
{
    YearlyFormula,
    AdminFull,
    AdminNone
}
