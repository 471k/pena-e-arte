using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// The caller's own appointments — resolved through Client.UserId (the JWT user id),
/// so this never exposes another client's schedule.
/// </summary>
public record GetMyAppointmentsQuery : IRequest<List<AppointmentResponse>>;

public class GetMyAppointmentsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyAppointmentsQuery, List<AppointmentResponse>>
{
    public async Task<List<AppointmentResponse>> Handle(GetMyAppointmentsQuery query, CancellationToken ct)
    {
        Domain.Entities.Client? me = await db.FindClientForUserAsync(currentUser, ct);
        if (me is null) return [];

        return await db.Appointments
            .Where(a => a.ClientId == me.Id)
            .OrderBy(a => a.Date)
            .Select(a => CreateAppointmentHandler.Map(a))
            .ToListAsync(ct);
    }
}
