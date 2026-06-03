using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Payments.Commands;

public record UpdateSessionSplitsCommand(Guid PaymentId, UpdateSessionSplitsRequest Request)
    : IRequest<PaymentResponse>;

public class UpdateSessionSplitsHandler(IAppDbContext db)
    : IRequestHandler<UpdateSessionSplitsCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(UpdateSessionSplitsCommand command, CancellationToken ct)
    {
        Payment? payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);
        if (payment is null)
            throw new NotFoundException(nameof(Payment), command.PaymentId);

        decimal sum = command.Request.Splits.Sum(s => s.Amount);
        if (sum != payment.Amount)
            throw new BusinessRuleViolationException(
                $"Session splits total ({sum:F2}) must equal payment amount ({payment.Amount:F2}).");

        List<SessionSplit> existing = await db.SessionSplits
            .Where(ss => ss.PaymentId == command.PaymentId)
            .ToListAsync(ct);

        foreach (SessionSplit ss in existing)
        {
            ss.DeletedAt = DateTime.UtcNow;
            ss.UpdatedAt = DateTime.UtcNow;
        }

        List<SessionSplit> newSplits = command.Request.Splits
            .Select(item => new SessionSplit
            {
                StudioId  = payment.StudioId,
                PaymentId = payment.Id,
                Label     = item.Label,
                Amount    = item.Amount
            }).ToList();

        db.SessionSplits.AddRange(newSplits);
        payment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        List<SessionSplitResponse> splitResponses = newSplits
            .Select(CreatePaymentIntentHandler.MapSplit).ToList();
        return CreatePaymentIntentHandler.Map(payment, splitResponses);
    }
}
