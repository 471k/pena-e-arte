using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.IntakeForms.Queries;

public record GetIntakeFormsQuery(Guid? ClientId, Guid? AppointmentId) : IRequest<List<IntakeFormResponse>>;

public class GetIntakeFormsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetIntakeFormsQuery, List<IntakeFormResponse>>
{
    public async Task<List<IntakeFormResponse>> Handle(GetIntakeFormsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.IntakeForm> q = db.IntakeForms;

        Guid? clientId = query.ClientId;
        if (currentUser.Role == "client")
        {
            Guid? myId = await db.Clients
                .Where(c => c.UserId == currentUser.UserId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (myId is null) return [];
            clientId = myId;
        }

        if (clientId.HasValue)            q = q.Where(f => f.ClientId      == clientId.Value);
        if (query.AppointmentId.HasValue) q = q.Where(f => f.AppointmentId == query.AppointmentId.Value);

        return await q
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => SubmitIntakeFormHandler.Map(f))
            .ToListAsync(ct);
    }
}
