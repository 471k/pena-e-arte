using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.IntakeForms.Queries;

public record GetIntakeFormByIdQuery(Guid Id) : IRequest<IntakeFormResponse>;

public class GetIntakeFormByIdHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetIntakeFormByIdQuery, IntakeFormResponse>
{
    public async Task<IntakeFormResponse> Handle(GetIntakeFormByIdQuery query, CancellationToken ct)
    {
        Domain.Entities.IntakeForm form = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.IntakeForm), query.Id);

        if (currentUser.Role == "client")
        {
            Guid? myId = await db.Clients
                .Where(c => c.UserId == currentUser.UserId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (myId is null || form.ClientId != myId.Value)
                throw new NotFoundException(nameof(Domain.Entities.IntakeForm), query.Id);
        }

        return SubmitIntakeFormHandler.Map(form);
    }
}
