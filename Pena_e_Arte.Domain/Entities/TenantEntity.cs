namespace Pena_e_Arte.Domain.Entities;

public abstract class TenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StudioId { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
