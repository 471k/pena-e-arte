namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Stable, human-readable audit action identifiers — never the raw C# command type
/// name, which is an implementation detail that could be renamed/refactored.
/// </summary>
public static class AuditActions
{
    public const string StudioSuspended               = "Studio.Suspended";
    public const string StudioUnsuspended              = "Studio.Unsuspended";
    public const string StudioTrialExtended             = "Studio.TrialExtended";
    public const string SubscriptionCancelledByIssuer   = "Subscription.CancelledByIssuer";
    public const string SubscriptionActivatedManually   = "Subscription.ActivatedManually";
    public const string PlanUpdated                     = "Plan.Updated";
    public const string ReferralCodeDeactivated         = "ReferralCode.Deactivated";
    public const string ReferralCodeReactivated          = "ReferralCode.Reactivated";
    public const string ReferralCodeDeleted             = "ReferralCode.Deleted";
    public const string AppointmentCancelled            = "Appointment.Cancelled";
    public const string SessionSplitsUpdated            = "SessionSplits.Updated";
}

/// <summary>Entity kind the audited action targets — paired with AuditLogEntry.TargetId.</summary>
public static class AuditTargetTypes
{
    public const string Studio       = "Studio";
    public const string Subscription = "Subscription";
    public const string Plan         = "Plan";
    public const string ReferralCode = "ReferralCode";
    public const string Appointment  = "Appointment";
    public const string Payment      = "Payment";
}
