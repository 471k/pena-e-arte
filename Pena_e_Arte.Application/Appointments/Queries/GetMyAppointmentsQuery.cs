using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// The caller's own appointments — resolved through Client.UserId (the JWT user id),
/// so this never exposes another client's schedule. Each appointment carries its self-service
/// policy (notice window, and whether the client is still within it), computed here by
/// ClientCancellationPolicy so the bookings list never needs a call per appointment and the
/// frontend never recomputes it (G-3/G-17).
/// </summary>
public record GetMyAppointmentsQuery : IRequest<List<AppointmentResponse>>;

public class GetMyAppointmentsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyAppointmentsQuery, List<AppointmentResponse>>
{
    public async Task<List<AppointmentResponse>> Handle(GetMyAppointmentsQuery query, CancellationToken ct)
    {
        Domain.Entities.Client? me = await db.FindClientForUserAsync(currentUser, ct);
        if (me is null) return [];

        List<AppointmentResponse> appointments = await db.Appointments
            .Where(a => a.ClientId == me.Id)
            .Include(a => a.Attachments)
            .OrderBy(a => a.Date)
            .Select(a => CreateAppointmentHandler.Map(a))
            .ToListAsync(ct);

        // The active rule, resolved exactly as CancelAppointmentHandler and RescheduleAppointmentHandler
        // resolve it for a client.
        DepositRule? rule = await db.DepositRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        int windowHours = ClientCancellationPolicy.NoticeWindowHours(rule);
        DateTime nowUtc = DateTime.UtcNow;

        return appointments
            .Select(a => a with
            {
                SelfService = new AppointmentSelfServiceResponse(
                    windowHours, ClientCancellationPolicy.IsWithinNoticeWindow(rule, a.Date, nowUtc)),
            })
            .ToList();
    }
}
