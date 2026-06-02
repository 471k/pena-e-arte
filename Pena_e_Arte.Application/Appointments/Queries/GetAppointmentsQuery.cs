using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record GetAppointmentsQuery(DateTime? From, DateTime? To) : IRequest<List<AppointmentResponse>>;

public class GetAppointmentsHandler(IAppDbContext db)
    : IRequestHandler<GetAppointmentsQuery, List<AppointmentResponse>>
{
    public async Task<List<AppointmentResponse>> Handle(GetAppointmentsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Appointment> q = db.Appointments;

        if (query.From.HasValue) q = q.Where(a => a.Date >= query.From.Value);
        if (query.To.HasValue)   q = q.Where(a => a.Date <= query.To.Value);

        return await q
            .OrderBy(a => a.Date)
            .Select(a => CreateAppointmentHandler.Map(a))
            .ToListAsync(ct);
    }
}
