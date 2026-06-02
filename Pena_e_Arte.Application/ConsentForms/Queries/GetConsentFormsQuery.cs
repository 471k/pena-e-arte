using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.ConsentForms.Queries;

public record GetConsentFormsQuery(Guid? ClientId, Guid? AppointmentId) : IRequest<List<ConsentFormResponse>>;

public class GetConsentFormsHandler(IAppDbContext db)
    : IRequestHandler<GetConsentFormsQuery, List<ConsentFormResponse>>
{
    public async Task<List<ConsentFormResponse>> Handle(GetConsentFormsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.ConsentForm> q = db.ConsentForms;

        if (query.ClientId.HasValue)      q = q.Where(f => f.ClientId      == query.ClientId.Value);
        if (query.AppointmentId.HasValue) q = q.Where(f => f.AppointmentId == query.AppointmentId.Value);

        return await q
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => SignConsentFormHandler.Map(f))
            .ToListAsync(ct);
    }
}
