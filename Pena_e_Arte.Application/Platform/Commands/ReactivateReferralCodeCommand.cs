using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record ReactivateReferralCodeCommand(Guid ReferralCodeId) : IRequest;

public class ReactivateReferralCodeHandler(IAppDbContext db)
    : IRequestHandler<ReactivateReferralCodeCommand>
{
    public async Task Handle(ReactivateReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #10 — issuer reactivates any
        // studio's referral code cross-tenant. See architecture.md.
        Domain.Entities.ReferralCode code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == command.ReferralCodeId, ct)
            ?? throw new NotFoundException(
                nameof(Domain.Entities.ReferralCode), command.ReferralCodeId);

        List<Domain.Entities.ReferralCode> others = await db.ReferralCodes
            .IgnoreQueryFilters()
            .Where(r => r.StudioId == code.StudioId && r.Id != code.Id && r.IsActive)
            .ToListAsync(ct);
        foreach (Domain.Entities.ReferralCode other in others)
            other.IsActive = false;

        code.IsActive = true;

        await db.SaveChangesAsync(ct);
    }
}

public class ReactivateReferralCodeValidator
    : AbstractValidator<ReactivateReferralCodeCommand>
{
    public ReactivateReferralCodeValidator()
    {
        RuleFor(x => x.ReferralCodeId).NotEmpty();
    }
}
