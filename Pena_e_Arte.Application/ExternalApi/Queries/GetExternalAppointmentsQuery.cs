using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.ExternalApi;

namespace Pena_e_Arte.Application.ExternalApi.Queries;

public record GetExternalAppointmentsQuery(int Page = 1, int PageSize = 50)
    : IRequest<List<ExternalAppointmentResponse>>;

public class GetExternalAppointmentsHandler(IAppDbContext db)
    : IRequestHandler<GetExternalAppointmentsQuery, List<ExternalAppointmentResponse>>
{
    private const int MaxPageSize = 200;

    public async Task<List<ExternalAppointmentResponse>> Handle(
        GetExternalAppointmentsQuery query, CancellationToken ct)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        return await db.Appointments
            .OrderByDescending(a => a.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ExternalAppointmentResponse(
                a.Id,
                a.Date,
                a.DurationMinutes,
                a.Status.ToString(),
                a.Client.FirstName + " " + a.Client.LastName,
                a.Artist != null ? a.Artist.FirstName + " " + a.Artist.LastName : null,
                a.DepositAmount,
                a.DepositStatus.ToString(),
                a.CreatedAt))
            .ToListAsync(ct);
    }
}
