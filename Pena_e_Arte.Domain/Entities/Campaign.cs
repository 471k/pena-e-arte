using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A marketing email campaign (email-only — SMS is explicitly out of scope, see
/// architecture.md Decisions Log "Marketing Email Campaigns (P1 #10)"). Audience
/// resolution always hard-filters on Client.MarketingOptIn == true regardless of
/// Audience — see SendCampaignCommand.
/// </summary>
public class Campaign : TenantEntity
{
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public CampaignAudience Audience { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    /// <summary>Only meaningful for Audience == ClientsWithNoRecentVisit — no completed
    /// appointment in this many days. Defaults to 90, owner-configurable per send.</summary>
    public int? NoRecentVisitDays { get; set; }

    /// <summary>Only meaningful for Audience == Custom — a starting set of client ids,
    /// still hard-filtered by MarketingOptIn (never a way to bypass consent).</summary>
    public List<Guid> CustomClientIds { get; set; } = [];

    public DateTime? SentAt { get; set; }
    public int RecipientCount { get; set; }
    public int DeliveredCount { get; set; }
}
