namespace Pena_e_Arte.Domain.Enums;

/// <summary>
/// Dimensions a Plan can cap. Checked by PlanLimitBehavior against the corresponding
/// Plan.Max* field (null on the Plan = unlimited, no check performed).
/// </summary>
public enum QuotaType
{
    Artists,
    AppointmentsPerMonth,
    NotificationsPerMonth,
    StorageBytes,
    Locations
}
