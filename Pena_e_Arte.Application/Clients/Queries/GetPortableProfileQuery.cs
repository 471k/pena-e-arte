using MediatR;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Models;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetPortableProfileQuery(Guid ClientUserId) : IRequest<PortableClientProfile?>;

public class GetPortableProfileHandler(IPortableProfileService portableProfileService)
    : IRequestHandler<GetPortableProfileQuery, PortableClientProfile?>
{
    public async Task<PortableClientProfile?> Handle(GetPortableProfileQuery query, CancellationToken ct)
    {
        PortableClientProfile? profile = await portableProfileService.FindByUserIdAsync(query.ClientUserId, ct);
        if (profile is null) return null;

        IReadOnlyList<PortableTattooRecord> history = await portableProfileService.GetHistoryAsync(query.ClientUserId, ct);
        return profile with { TattooHistory = history };
    }
}
