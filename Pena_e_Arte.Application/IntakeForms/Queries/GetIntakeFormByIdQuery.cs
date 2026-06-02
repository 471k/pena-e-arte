using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.IntakeForms.Queries;

public record GetIntakeFormByIdQuery(Guid Id) : IRequest<IntakeFormResponse>;

public class GetIntakeFormByIdHandler(IAppDbContext db)
    : IRequestHandler<GetIntakeFormByIdQuery, IntakeFormResponse>
{
    public async Task<IntakeFormResponse> Handle(GetIntakeFormByIdQuery query, CancellationToken ct)
    {
        Domain.Entities.IntakeForm form = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.IntakeForm), query.Id);

        return SubmitIntakeFormHandler.Map(form);
    }
}
