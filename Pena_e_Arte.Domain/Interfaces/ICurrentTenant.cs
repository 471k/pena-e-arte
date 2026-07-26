namespace Pena_e_Arte.Domain.Interfaces;

public interface ICurrentTenant
{
    Guid StudioId { get; }
    bool IsSet { get; }
    void SetTenant(Guid studioId);
}
