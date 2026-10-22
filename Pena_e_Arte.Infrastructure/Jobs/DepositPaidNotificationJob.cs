using MediatR;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Sends the client the "deposit received" email/SMS for a card deposit that turned Paid without the
/// owner pressing "Capture deposit": since C-4 (2026-10-22) card deposits are charged when the client
/// pays, so the transition happens in the confirm call, the deposit-resume heal or
/// PaymentReconciliationJob. Its own job, so a mail failure never touches those. Enqueued once, on
/// the transition to Paid. SendDepositCapturedNotificationCommand reads the payment by its own id
/// across tenants, so it works outside a request.
/// </summary>
public class DepositPaidNotificationJob(ISender sender)
{
    public Task ExecuteAsync(Guid paymentId, CancellationToken ct = default) =>
        sender.Send(new SendDepositCapturedNotificationCommand(paymentId), ct);
}
