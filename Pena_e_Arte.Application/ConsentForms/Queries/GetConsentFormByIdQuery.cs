using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.ConsentForms.Queries;

public record GetConsentFormByIdQuery(Guid Id) : IRequest<ConsentFormResponse>;

public class GetConsentFormByIdHandler(IAppDbContext db)
    : IRequestHandler<GetConsentFormByIdQuery, ConsentFormResponse>
{
    public async Task<ConsentFormResponse> Handle(GetConsentFormByIdQuery query, CancellationToken ct)
    {
        Domain.Entities.ConsentForm form = await db.ConsentForms
            .FirstOrDefaultAsync(f => f.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.ConsentForm), query.Id);

        return SignConsentFormHandler.Map(form);
    }
}
