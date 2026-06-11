using Pena_e_Arte.Domain.Models;

namespace Pena_e_Arte.Domain.Interfaces;

public interface IPortableProfileService
{
    Task<PortableClientProfile?>              FindByUserIdAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<PortableTattooRecord>> GetHistoryAsync(Guid userId, CancellationToken ct);
}
