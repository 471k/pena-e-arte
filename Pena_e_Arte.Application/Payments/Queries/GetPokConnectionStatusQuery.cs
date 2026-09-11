using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPokConnectionStatusQuery : IRequest<PokConnectionStatusResponse>;

public class GetPokConnectionStatusHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetPokConnectionStatusQuery, PokConnectionStatusResponse>
{
    public async Task<PokConnectionStatusResponse> Handle(GetPokConnectionStatusQuery query, CancellationToken ct)
    {
        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct);
        bool hasCredentialRef = await db.StudioCredentialRefs
            .AnyAsync(c => c.StudioId == tenant.StudioId && c.Provider == CredentialProvider.Pok, ct);

        bool connected = studio?.PokMerchantId is not null && hasCredentialRef;
        return new PokConnectionStatusResponse(connected, connected ? studio!.PokMerchantId : null);
    }
}
