namespace Pena_e_Arte.Domain.Enums;

/// <summary>
/// Why a manual reminder ended up <see cref="ManualReminderStatus.Failed"/>. A category only —
/// never raw provider error text, which can carry phone numbers or other PII.
/// </summary>
public enum ManualReminderFailureReason
{
    /// <summary>The client has opted out of SMS, so nothing was sent.</summary>
    SmsOptOut,

    /// <summary>The studio had already used its plan's notifications-per-month allowance.</summary>
    PlanLimit,

    /// <summary>A previous send attempt has no recorded outcome (crash retry); not re-sent to avoid texting twice.</summary>
    UnknownOutcome,

    /// <summary>The SMS provider rejected or failed the send.</summary>
    ProviderError
}
