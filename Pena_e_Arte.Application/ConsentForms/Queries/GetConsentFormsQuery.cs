using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConsentForms.Queries;

public record GetConsentFormsQuery(Guid? ClientId, Guid? AppointmentId) : IRequest<List<ConsentFormResponse>>;

public class GetConsentFormsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetConsentFormsQuery, List<ConsentFormResponse>>
{
    public async Task<List<ConsentFormResponse>> Handle(GetConsentFormsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.ConsentForm> q = db.ConsentForms;

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
            .Select(f => new ConsentFormResponse(
                f.Id,
                f.StudioId,
                f.ClientId,
                f.AppointmentId,
                f.FileUrl,
                f.SignatureData,
                f.SignedAt,
                f.CreatedAt,
                f.Client.FirstName + " " + f.Client.LastName))
            .ToListAsync(ct);
    }
}
