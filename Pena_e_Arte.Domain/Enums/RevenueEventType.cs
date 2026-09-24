namespace Pena_e_Arte.Domain.Enums;

/// <summary>What kind of MRR-affecting transition a SubscriptionRevenueEvent records. New/
/// Expansion/Contraction/Churn/Reactivation are the five ChartMogul-style movement types;
/// Paused/Resumed/PastDue/Recovered are billing-state transitions that change whether the
/// contract counts toward MRR without changing its amount.</summary>
public enum RevenueEventType
{
    New,
    Expansion,
    Contraction,
    Churn,
    Reactivation,
    Paused,
    Resumed,
    PastDue,
    Recovered
}
