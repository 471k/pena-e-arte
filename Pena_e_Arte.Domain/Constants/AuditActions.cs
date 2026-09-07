namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Stable, human-readable audit action identifiers — never the raw C# command type
/// name, which is an implementation detail that could be renamed/refactored.
/// </summary>
public static class AuditActions
{
    public const string StudioSuspended = "Studio.Suspended";
    public const string StudioUnsuspended = "Studio.Unsuspended";
    public const string StudioTrialExtended = "Studio.TrialExtended";
    public const string SubscriptionCancelledByAdmin = "Subscription.CancelledByAdmin";
    public const string SubscriptionActivatedManually = "Subscription.ActivatedManually";
    public const string PlanUpdated = "Plan.Updated";
    public const string ReferralCodeDeactivated = "ReferralCode.Deactivated";
    public const string ReferralCodeReactivated = "ReferralCode.Reactivated";
    public const string ReferralCodeDeleted = "ReferralCode.Deleted";
    public const string AppointmentCancelled = "Appointment.Cancelled";
    public const string AppointmentArtistAssigned = "Appointment.ArtistAssigned";
    public const string SessionSplitsUpdated = "SessionSplits.Updated";
    public const string ClientProfileCrossTenantOptedIn = "ClientProfile.CrossTenantOptedIn";
    public const string ClientProfileCrossTenantOptedOut = "ClientProfile.CrossTenantOptedOut";

    /// <summary>Client-initiated (or support-initiated) right-to-erasure request. Distinct
    /// from the policy-driven automatic retention purge, which is not an audited command.</summary>
    public const string ClientDataErasureRequested = "Client.DataErasureRequested";

    public const string ClientArtistReassigned = "Client.ArtistReassigned";

    public const string ManualReminderSent = "ManualReminder.Sent";
    public const string ManualReminderCancelled = "ManualReminder.Cancelled";

    public const string ConductReportStatusUpdated = "ConductReport.StatusUpdated";

    public const string PaymentRefunded = "Payment.Refunded";
    public const string CashDepositConfirmed = "Payment.CashDepositConfirmed";

    /// <summary>One-time creation of the platform's first admin account by AdminBootstrapper,
    /// never a MediatR command — see AdminBootstrapper.RunAsync.</summary>
    public const string AdminAccountBootstrapped = "Admin.AccountBootstrapped";
}

/// <summary>Entity kind the audited action targets — paired with AuditLogEntry.TargetId.</summary>
public static class AuditTargetTypes
{
    public const string Studio = "Studio";
    public const string Subscription = "Subscription";
    public const string Plan = "Plan";
    public const string ReferralCode = "ReferralCode";
    public const string Appointment = "Appointment";
    public const string Payment = "Payment";
    public const string ClientProfile = "ClientProfile";
    public const string Client = "Client";
    public const string ManualReminder = "ManualReminder";
    public const string ConductReport = "ConductReport";
    public const string User = "User";
}
