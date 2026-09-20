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
    public const string SubscriptionDunningExclusionChanged = "Subscription.DunningExclusionChanged";
    public const string PlanUpdated = "Plan.Updated";
    public const string ReferralCodeDeactivated = "ReferralCode.Deactivated";
    public const string ReferralCodeReactivated = "ReferralCode.Reactivated";
    public const string ReferralCodeDeleted = "ReferralCode.Deleted";
    public const string AppointmentCancelled = "Appointment.Cancelled";
    public const string AppointmentRescheduled = "Appointment.Rescheduled";
    public const string AppointmentArtistAssigned = "Appointment.ArtistAssigned";
    public const string SessionSplitsUpdated = "SessionSplits.Updated";
    public const string ClientProfileCrossTenantOptedIn = "ClientProfile.CrossTenantOptedIn";
    public const string ClientProfileCrossTenantOptedOut = "ClientProfile.CrossTenantOptedOut";

    /// <summary>Client-initiated (or support-initiated) right-to-erasure request. Distinct
    /// from the policy-driven automatic retention purge, which is not an audited command.</summary>
    public const string ClientDataErasureRequested = "Client.DataErasureRequested";

    public const string ClientArtistReassigned = "Client.ArtistReassigned";
    /// <summary>A client edited their own name/phone. Metadata carries only the affected row count —
    /// never the values (PII).</summary>
    public const string ClientSelfProfileUpdated = "Client.SelfProfileUpdated";
    public const string ClientArchived = "Client.Archived";
    public const string ClientRestored = "Client.Restored";
    public const string ClientDataErasureCancelled = "Client.DataErasureCancelled";

    public const string ManualReminderSent = "ManualReminder.Sent";
    public const string ManualReminderCancelled = "ManualReminder.Cancelled";

    public const string ConductReportStatusUpdated = "ConductReport.StatusUpdated";

    public const string PaymentRefunded = "Payment.Refunded";
    public const string CashDepositConfirmed = "Payment.CashDepositConfirmed";
    public const string PokAccountConnected = "Studio.PokAccountConnected";

    public const string WaitlistEntryCancelled = "WaitlistEntry.Cancelled";

    public const string GiftCardRedeemed = "GiftCard.Redeemed";
    public const string GiftCardVoided = "GiftCard.Voided";

    /// <summary>One-time creation of the platform's first admin account by AdminBootstrapper,
    /// never a MediatR command — see AdminBootstrapper.RunAsync.</summary>
    public const string AdminAccountBootstrapped = "Admin.AccountBootstrapped";

    public const string CampaignSent = "Campaign.Sent";
    public const string ImpersonationSessionStarted = "ImpersonationSession.Started";
    public const string ImpersonationSessionEnded = "ImpersonationSession.Ended";

    /// <summary>Social/Instagram link changes on an artist or studio. Metadata carries only the
    /// platform — never the handle (PII). Verification is "Attempted" because the audit pipeline
    /// logs any non-throwing run, including a bio-code check that didn't find the code.</summary>
    public const string SocialHandleUpdated = "SocialLink.HandleUpdated";
    public const string SocialVerificationRequested = "SocialLink.VerificationRequested";
    public const string SocialVerificationAttempted = "SocialLink.VerificationAttempted";
    public const string SocialDisconnected = "SocialLink.Disconnected";
    public const string SocialConnectedViaOAuth = "SocialLink.ConnectedViaOAuth";
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
    public const string WaitlistEntry = "WaitlistEntry";
    public const string GiftCard = "GiftCard";
    public const string Campaign = "Campaign";
    public const string ImpersonationSession = "ImpersonationSession";
    public const string Artist = "Artist";
}
