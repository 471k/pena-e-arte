using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record GetAppointmentQuery(Guid AppointmentId) : IRequest<AppointmentResponse>;

public class GetAppointmentHandler(IAppDbContext db)
    : IRequestHandler<GetAppointmentQuery, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(GetAppointmentQuery query, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), query.AppointmentId);

        return CreateAppointmentHandler.Map(appointment);
    }
}
