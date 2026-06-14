using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConsentForms.Queries;

public record GetConsentFormByIdQuery(Guid Id) : IRequest<ConsentFormResponse>;

public class GetConsentFormByIdHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetConsentFormByIdQuery, ConsentFormResponse>
{
    public async Task<ConsentFormResponse> Handle(GetConsentFormByIdQuery query, CancellationToken ct)
    {
        Domain.Entities.ConsentForm form = await db.ConsentForms
            .FirstOrDefaultAsync(f => f.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.ConsentForm), query.Id);

        if (currentUser.Role == "client")
        {
            Guid? myId = await db.Clients
                .Where(c => c.UserId == currentUser.UserId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (myId is null || form.ClientId != myId.Value)
                throw new NotFoundException(nameof(Domain.Entities.ConsentForm), query.Id);
        }

        return SignConsentFormHandler.Map(form);
    }
}
