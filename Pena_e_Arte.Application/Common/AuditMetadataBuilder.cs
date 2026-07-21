using System.Text.Json;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Application.Platform.Commands;

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
        UpdatePlanCommand c => new Dictionary<string, object?>
        {
            // Plan name is a studio-facing product label, not PII.
            ["planName"] = c.Request.Name,
        },
        _ => new Dictionary<string, object?>(),
    });

    private static string Serialize(Dictionary<string, object?> data) => JsonSerializer.Serialize(data);
}
