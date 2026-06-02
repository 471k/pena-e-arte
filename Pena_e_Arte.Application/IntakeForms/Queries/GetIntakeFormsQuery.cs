using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.IntakeForms.Queries;

public record GetIntakeFormsQuery(Guid? ClientId, Guid? AppointmentId) : IRequest<List<IntakeFormResponse>>;

public class GetIntakeFormsHandler(IAppDbContext db)
    : IRequestHandler<GetIntakeFormsQuery, List<IntakeFormResponse>>
{
    public async Task<List<IntakeFormResponse>> Handle(GetIntakeFormsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.IntakeForm> q = db.IntakeForms;

        if (query.ClientId.HasValue)      q = q.Where(f => f.ClientId      == query.ClientId.Value);
        if (query.AppointmentId.HasValue) q = q.Where(f => f.AppointmentId == query.AppointmentId.Value);

        return await q
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => SubmitIntakeFormHandler.Map(f))
            .ToListAsync(ct);
    }
}
