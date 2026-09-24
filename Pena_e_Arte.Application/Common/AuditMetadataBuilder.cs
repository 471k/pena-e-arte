using System.Text.Json;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.GiftCards.Commands;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Application.Support.Commands;
using Pena_e_Arte.Application.Waitlists.Commands;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// Builds the PII-scrubbed Metadata JSON for an audited command. Whitelists fields per
/// concrete command type — deliberately does NOT serialize the command wholesale, since
/// that is exactly how a name/email/note field could leak into the audit log by accident.
/// Only IDs, enum values, and structural before/after values belong here (CLAUDE.md rule #3).
/// </summary>
public static class AuditMetadataBuilder
{
    public static string Build(object command) => Serialize(command switch
    {
        ExtendTrialCommand c => new Dictionary<string, object?>
        {
            ["additionalDays"] = c.Request.AdditionalDays,
        },
        ActivateSubscriptionManuallyCommand c => new Dictionary<string, object?>
        {
            ["planId"] = c.PlanId,
        },
        // Yearly-cancellation-refund amounts/counts only — never names, never card data (rule #3).
        CancelSubscriptionCommand c => new Dictionary<string, object?>
        {
            ["rule"] = c.ComputedRule,
            ["amount"] = c.ComputedRefundAmount,
            ["monthsUsed"] = c.ComputedMonthsUsed,
            ["reason"] = c.OverrideReason,
        },
        CancelMySubscriptionCommand c => new Dictionary<string, object?>
        {
            ["amount"] = c.ComputedRefundAmount,
            ["monthsUsed"] = c.ComputedMonthsUsed,
        },
        UpdatePlanCommand c => new Dictionary<string, object?>
        {
            // Plan name is a studio-facing product label, not PII.
            ["planName"] = c.Request.Name,
        },
        RescheduleAppointmentCommand c => new Dictionary<string, object?>
        {
            // Scheduling facts only, no client/artist names or notes — matches D24's
            // "who changed what" intent without risking a PII leak into the audit log.
            ["newDate"] = c.Request.NewDate,
            ["newDurationMinutes"] = c.Request.NewDurationMinutes,
        },
        CancelWaitlistEntryCommand c => new Dictionary<string, object?>
        {
            ["waitlistEntryId"] = c.WaitlistEntryId,
        },
        RedeemGiftCardCommand c => new Dictionary<string, object?>
        {
            // Amount and appointment id only — no purchaser/recipient email.
            ["appointmentId"] = c.Request.AppointmentId,
            ["amount"] = c.Request.Amount,
        },
        VoidGiftCardCommand c => new Dictionary<string, object?>
        {
            ["giftCardId"] = c.Id,
        },
        StartImpersonationCommand c => new Dictionary<string, object?>
        {
            // Free-text reason code — a support-entered justification, not PII about the
            // studio/client, so it's fine in the audit log same as ActivateSubscriptionManuallyCommand's note.
            ["reasonCode"] = c.Request.ReasonCode,
        },
        EndImpersonationSessionCommand c => new Dictionary<string, object?>
        {
            ["sessionId"] = c.SessionId,
        },
        RequestDataErasureCommand c => new Dictionary<string, object?>
        {
            // How many studios' Client rows were fanned out to — never the ids, never PII.
            ["affectedClientCount"] = c.AffectedClientCount,
        },
        RequestMyDataErasureCommand c => new Dictionary<string, object?>
        {
            ["affectedClientCount"] = c.AffectedClientCount,
        },
        UpdateMyClientCommand c => new Dictionary<string, object?>
        {
            // Row count only — the new name/phone are PII and never go in the audit log.
            ["affectedClientCount"] = c.AffectedClientCount,
        },
        // Social link changes: the platform only. The handle is a person's public identity and never
        // goes in the audit log, nor does any verification code.
        UpdateSocialHandleCommand c => new Dictionary<string, object?> { ["platform"] = c.Platform.ToString() },
        RequestSocialVerificationCodeCommand c => new Dictionary<string, object?> { ["platform"] = c.Platform.ToString() },
        VerifySocialBioCodeCommand c => new Dictionary<string, object?> { ["platform"] = c.Platform.ToString() },
        DisconnectSocialAccountCommand c => new Dictionary<string, object?> { ["platform"] = c.Platform.ToString() },
        DisconnectInstagramCommand => new Dictionary<string, object?> { ["platform"] = "Instagram" },
        _ => new Dictionary<string, object?>(),
    });

    private static string Serialize(Dictionary<string, object?> data) => JsonSerializer.Serialize(data);
}
