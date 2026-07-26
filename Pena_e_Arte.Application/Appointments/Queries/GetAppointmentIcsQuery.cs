using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record GetAppointmentIcsQuery(Guid AppointmentId) : IRequest<string>;

public class GetAppointmentIcsHandler(IAppDbContext db)
    : IRequestHandler<GetAppointmentIcsQuery, string>
{
    public async Task<string> Handle(GetAppointmentIcsQuery query, CancellationToken ct)
    {
        var appt = await db.Appointments
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException("Appointment", query.AppointmentId);

        string dtStart = appt.Date.ToString("yyyyMMddTHHmmssZ");
        string dtEnd = appt.EndDate.ToString("yyyyMMddTHHmmssZ");
        string dtStamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string uid = $"{appt.Id}@tattooos.co";
        string summary = $"Tattoo appointment";
        string artist = appt.Artist is not null
            ? $" with {appt.Artist.FirstName} {appt.Artist.LastName}".Trim()
            : string.Empty;

        return $"""
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//TattooOS//Appointment//EN
BEGIN:VEVENT
UID:{uid}
DTSTAMP:{dtStamp}
DTSTART:{dtStart}
DTEND:{dtEnd}
SUMMARY:{summary}{artist}
DESCRIPTION:Deposit: {appt.DepositAmount:F2} EUR
END:VEVENT
END:VCALENDAR
""";
    }
}
