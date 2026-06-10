namespace Pena_e_Arte.Domain.Entities;

public class Studio
{
    public Guid     Id              { get; init; } = Guid.NewGuid();
    public string   Name            { get; set; }  = string.Empty;
    public string   Slug            { get; set; }  = string.Empty;
    public string   City            { get; set; }  = string.Empty;
    public string   OwnerEmail      { get; set; }  = string.Empty;
    public double   Latitude        { get; set; }
    public double   Longitude       { get; set; }
    public bool     IsActive              { get; set; }
    public bool     ShowPlatformBranding  { get; set; } = true;
    public DateTime TrialExpiresAt        { get; set; }
    public string?  StripeAccountId  { get; set; }
    public string?  StripeCustomerId { get; set; }
    public DateTime CreatedAt       { get; init; } = DateTime.UtcNow;

    public Subscription? Subscription { get; set; }
}
