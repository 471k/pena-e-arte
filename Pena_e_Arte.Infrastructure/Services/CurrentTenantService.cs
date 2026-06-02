using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class CurrentTenantService : ICurrentTenant
{
    private Guid _studioId;

    public Guid StudioId => _studioId;
    public bool IsSet    => _studioId != Guid.Empty;

    public void SetTenant(Guid studioId) => _studioId = studioId;
}
