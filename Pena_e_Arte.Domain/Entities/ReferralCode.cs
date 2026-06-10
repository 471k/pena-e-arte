namespace Pena_e_Arte.Domain.Entities;

public class ReferralCode
{
    public Guid      Id           { get; init; } = Guid.NewGuid();
    public Guid      StudioId     { get; set; }
    public string    Code         { get; set; }  = string.Empty;
    public bool      IsActive     { get; set; }  = true;
    public bool      IsSingleUse  { get; set; }  = true;
    public DateTime  CreatedAt    { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAt    { get; set; }
}
