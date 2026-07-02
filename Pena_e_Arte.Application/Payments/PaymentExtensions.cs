using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Payments;

internal static class PaymentExtensions
{
    internal static PaymentResponse ToResponse(this Payment p, IEnumerable<SessionSplit>? splits = null) => new(
        p.Id, p.AppointmentId, p.Amount,
        p.Status.ToString(), p.Method.ToString(),
        p.StripePaymentIntentId, p.ClientSecret, p.CashNote, p.PaidAt,
        $"{p.Client?.FirstName} {p.Client?.LastName}".Trim(),
        p.Appointment?.Date, // null when the navigation wasn't loaded
        (splits ?? p.SessionSplits).Select(s =>
            new SessionSplitResponse(s.Id, s.PaymentId, s.Label, s.Amount, s.PaidAt)).ToList());
}
