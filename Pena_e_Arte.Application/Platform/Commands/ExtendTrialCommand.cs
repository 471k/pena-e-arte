using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record ExtendTrialCommand(Guid StudioId, ExtendTrialRequest Request) : IRequest;

public class ExtendTrialHandler(IAppDbContext db)
    : IRequestHandler<ExtendTrialCommand>
{
    public async Task Handle(ExtendTrialCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StudioId == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Subscription), command.StudioId);

        if (subscription.Status != SubscriptionStatus.Trialing)
            throw new BusinessRuleViolationException("Trial extension is only allowed for studios in Trialing status.");

        subscription.TrialExpiresAt = subscription.TrialExpiresAt.AddDays(command.Request.AdditionalDays);

        await db.SaveChangesAsync(ct);
    }
}

public class ExtendTrialValidator : AbstractValidator<ExtendTrialCommand>
{
    public ExtendTrialValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Request.AdditionalDays).InclusiveBetween(1, 90);
    }
}
