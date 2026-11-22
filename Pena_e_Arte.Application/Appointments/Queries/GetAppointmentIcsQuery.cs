using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record GetAppointmentIcsQuery(Guid AppointmentId) : IRequest<string>;

public class GetAppointmentIcsHandler(IAppDbContext db, IExchangeRateProvider rates, ICurrentUser currentUser)
    : IRequestHandler<GetAppointmentIcsQuery, string>
{
    public async Task<string> Handle(GetAppointmentIcsQuery query, CancellationToken ct)
    {
        var appt = await db.Appointments
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException("Appointment", query.AppointmentId);

        // A client gets only their own appointment's file, by the same gate as GetMyAppointmentQuery.
        // Someone else's is 404, the same as a missing id, never 403: a 403 would confirm the appointment
        // exists. Until 14 Nov 2026 any signed-in client of the studio could fetch any appointment's file
        // (date, artist, deposit) by id. Staff are unchanged: the tenant filter already limits them to
        // their own studio.
        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appt.ClientId)
                throw new NotFoundException("Appointment", query.AppointmentId);
        }

        var studio = await db.Studios
            .Where(s => s.Id == appt.StudioId)
            .Select(s => new { s.CountryCode, s.Currency })
            .SingleAsync(ct);
        string studioCurrency = studio.Currency;
        string deposit = await FormatDepositAsync(appt.Id, appt.DepositAmount, studio.CountryCode, studioCurrency, ct);

        string dtStart = appt.Date.ToString("yyyyMMddTHHmmssZ");
        string dtEnd = appt.EndDate.ToString("yyyyMMddTHHmmssZ");
        string dtStamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string uid = $"{appt.Id}@tattooos.co";
        string summary = $"Tattoo appointment";
        string artist = appt.Artist is not null
            ? $" with {appt.Artist.FirstName} {appt.Artist.LastName}".Trim()
            : string.Empty;

        string status = IcsStatus(appt.Status);

        // STATUS says what the booking is at the moment of download, so a request imports as tentative and a
        // cancelled booking as cancelled instead of both looking confirmed (before 22 Nov 2026 there was no
        // STATUS line at all). What it does NOT do: retract an event someone already imported. Removing an
        // imported event needs METHOD:CANCEL on the calendar and a higher SEQUENCE, sent to the person. We only
        // ever offer this file as a download (a PUBLISH-style import, no METHOD line), never send it, so a stale
        // entry in someone's calendar stays until they delete it. SEQUENCE:0 is the field a future cancel-send
        // would have to increment.
        return $"""
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//TattooOS//Appointment//EN
BEGIN:VEVENT
UID:{uid}
SEQUENCE:0
DTSTAMP:{dtStamp}
DTSTART:{dtStart}
DTEND:{dtEnd}
STATUS:{status}
SUMMARY:{summary}{artist}
DESCRIPTION:Deposit: {deposit}
END:VEVENT
END:VCALENDAR
""";
    }

    // Every AppointmentStatus is named, so a new one fails here (and in the test that walks the enum) rather
    // than silently importing as confirmed. Completed and NoShow are CONFIRMED: the slot was held, and the file
    // records the booking, not attendance.
    private static string IcsStatus(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => "TENTATIVE",
        AppointmentStatus.Confirmed => "CONFIRMED",
        AppointmentStatus.Cancelled => "CANCELLED",
        AppointmentStatus.Completed => "CONFIRMED",
        AppointmentStatus.NoShow => "CONFIRMED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No iCalendar STATUS for this appointment status."),
    };

    // Albanian EUR studios show lek next to the deposit (spec D7): the lek frozen on the deposit payment
    // once the client has paid, otherwise the latest BoA rate, marked approximate. Every other studio:
    // exactly MoneyText.Format, as before.
    private async Task<string> FormatDepositAsync(
        Guid appointmentId, decimal depositAmount, string countryCode, string currency, CancellationToken ct)
    {
        if (!ReferenceAmount.AppliesTo(countryCode, currency))
            return MoneyText.Format(depositAmount, currency);

        var frozen = await db.Payments
            .Where(p => p.AppointmentId == appointmentId && p.ReferenceAmount != null)
            .Select(p => new { p.Amount, p.ReferenceAmount, p.ReferenceCurrency })
            .FirstOrDefaultAsync(ct);
        if (frozen is not null && frozen.Amount == depositAmount)
            return MoneyText.FormatWithReference(
                depositAmount, currency, frozen.ReferenceAmount, frozen.ReferenceCurrency, approximate: false);

        ExchangeRateSnapshot? latest = await rates.GetLatestAsync(
            ReferenceAmount.BaseCurrency, ReferenceAmount.ReferenceCurrency, ct);
        return latest is null
            ? MoneyText.Format(depositAmount, currency)
            : MoneyText.FormatWithReference(depositAmount, currency,
                ReferenceAmount.ToWholeLek(depositAmount, latest.Rate), ReferenceAmount.ReferenceCurrency, approximate: true);
    }
}
