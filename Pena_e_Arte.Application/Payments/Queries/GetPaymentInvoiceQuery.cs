using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentInvoiceQuery(Guid PaymentId) : IRequest<byte[]>;

public class GetPaymentInvoiceHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IPaymentInvoiceService invoiceService)
    : IRequestHandler<GetPaymentInvoiceQuery, byte[]>
{
    public async Task<byte[]> Handle(GetPaymentInvoiceQuery query, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.Client)
            .Include(p => p.SessionSplits)
            .FirstOrDefaultAsync(p => p.Id == query.PaymentId, ct);

        if (payment is null)
            throw new NotFoundException(nameof(Payment), query.PaymentId);

        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != payment.ClientId)
                throw new NotFoundException(nameof(Payment), query.PaymentId);
        }

        string studioName = await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        // Appointment and Artist loaded separately — Appointment is a required nav on Payment,
        // so Including it in the same query produces an inner join in InMemory and real DBs alike.
        Appointment? appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);

        Artist? artist = appointment is not null
            ? await db.Artists.FirstOrDefaultAsync(a => a.Id == appointment.ArtistId, ct)
            : null;

        IReadOnlyList<InvoiceLineItem> lineItems = payment.SessionSplits.Count > 0
            ? payment.SessionSplits
                     .OrderBy(s => s.CreatedAt)
                     .Select(s => new InvoiceLineItem(s.Label, s.Amount))
                     .ToList()
            : [new InvoiceLineItem("Tattoo deposit", payment.Amount)];

        PaymentInvoiceData data = new(
            StudioName: studioName,
            ClientFullName: $"{payment.Client?.FirstName} {payment.Client?.LastName}".Trim(),
            ClientEmail: payment.Client?.Email ?? string.Empty,
            ArtistFullName: artist is not null
                                       ? $"{artist.FirstName} {artist.LastName}".Trim()
                                       : "—",
            AppointmentDate: appointment?.Date ?? DateTime.UtcNow,
            PaymentId: payment.Id,
            TotalAmount: payment.Amount,
            Method: payment.Method.ToString(),
            Status: payment.Status.ToString(),
            StripePaymentIntentId: payment.StripePaymentIntentId,
            CashNote: payment.CashNote,
            IssuedAt: payment.PaidAt ?? DateTime.UtcNow,
            LineItems: lineItems);

        return invoiceService.Generate(data);
    }
}
