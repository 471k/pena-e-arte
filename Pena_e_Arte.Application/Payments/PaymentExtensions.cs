using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Payments;

internal static class PaymentExtensions
{
    /// <summary>
    /// What a payment actually contributes to revenue/earnings: the full amount minus
    /// whatever was refunded. A partial refund (e.g. a late self-cancellation with a
    /// studio-configured partial-refund percentage) still leaves Status == Refunded — there
    /// is no separate "PartiallyRefunded" status — so the retained portion must still count.
    /// </summary>
    internal static decimal RetainedAmount(this Payment p) => Math.Max(0m, p.Amount - (p.RefundedAmount ?? 0m));

    internal static PaymentResponse ToResponse(this Payment p, IEnumerable<SessionSplit>? splits = null) => new(
        p.Id, p.AppointmentId, p.Amount,
        p.Status.ToString(), p.Method.ToString(),
        p.ProviderReferenceId, p.ClientSecret, p.CashNote, p.PaidAt,
        $"{p.Client?.FirstName} {p.Client?.LastName}".Trim(),
        p.Appointment?.Date, // null when the navigation wasn't loaded
        (splits ?? p.SessionSplits).Select(s =>
            new SessionSplitResponse(s.Id, s.PaymentId, s.Label, s.Amount, s.PaidAt)).ToList());
}
