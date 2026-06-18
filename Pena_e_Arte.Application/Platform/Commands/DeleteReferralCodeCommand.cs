using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record DeleteReferralCodeCommand(Guid ReferralCodeId) : IRequest;

public class DeleteReferralCodeHandler(IAppDbContext db)
    : IRequestHandler<DeleteReferralCodeCommand>
{
    public async Task Handle(DeleteReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #11 — issuer deletes any
        // studio's unredeemed referral code cross-tenant. See architecture.md.
        Domain.Entities.ReferralCode code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .Include(r => r.Redemptions)
            .FirstOrDefaultAsync(r => r.Id == command.ReferralCodeId, ct)
            ?? throw new NotFoundException(
                nameof(Domain.Entities.ReferralCode), command.ReferralCodeId);

        if (code.Redemptions.Count > 0)
            throw new BusinessRuleViolationException(
                "Cannot delete a referral code that has been redeemed. Deactivate it instead.");

        db.ReferralCodes.Remove(code);
        await db.SaveChangesAsync(ct);
    }
}

public class DeleteReferralCodeValidator
    : AbstractValidator<DeleteReferralCodeCommand>
{
    public DeleteReferralCodeValidator()
    {
        RuleFor(x => x.ReferralCodeId).NotEmpty();
    }
}
