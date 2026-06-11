using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record DeactivateReferralCodeCommand(Guid ReferralCodeId) : IRequest;

public class DeactivateReferralCodeHandler(IAppDbContext db)
    : IRequestHandler<DeactivateReferralCodeCommand>
{
    public async Task Handle(DeactivateReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #6 — referral deactivation cross-tenant, IssuerOnly. See architecture.md.
        Domain.Entities.ReferralCode code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == command.ReferralCodeId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.ReferralCode), command.ReferralCodeId);

        code.IsActive = false;

        await db.SaveChangesAsync(ct);
    }
}

public class DeactivateReferralCodeValidator : AbstractValidator<DeactivateReferralCodeCommand>
{
    public DeactivateReferralCodeValidator()
    {
        RuleFor(x => x.ReferralCodeId).NotEmpty();
    }
}
