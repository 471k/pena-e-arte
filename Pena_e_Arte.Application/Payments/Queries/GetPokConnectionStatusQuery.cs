using MediatR;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPokConnectionStatusQuery : IRequest<PokConnectionStatusResponse>;

public class GetPokConnectionStatusHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetPokConnectionStatusQuery, PokConnectionStatusResponse>
{
    public async Task<PokConnectionStatusResponse> Handle(GetPokConnectionStatusQuery query, CancellationToken ct)
    {
        (bool connected, string? merchantId) = await PokConnectionCheck.ResolveAsync(db, tenant.StudioId, ct);
        return new PokConnectionStatusResponse(connected, merchantId);
    }
}
