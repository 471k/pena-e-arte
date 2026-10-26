using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record GetAppointmentIcsQuery(Guid AppointmentId) : IRequest<string>;

public class GetAppointmentIcsHandler(IAppDbContext db, IExchangeRateProvider rates)
    : IRequestHandler<GetAppointmentIcsQuery, string>
{
    public async Task<string> Handle(GetAppointmentIcsQuery query, CancellationToken ct)
    {
        var appt = await db.Appointments
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException("Appointment", query.AppointmentId);

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
DESCRIPTION:Deposit: {deposit}
END:VEVENT
END:VCALENDAR
""";
    }

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
