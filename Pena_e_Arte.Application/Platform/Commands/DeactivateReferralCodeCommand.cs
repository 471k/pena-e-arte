using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

// The command only carries ReferralCodeId, not the code's StudioId — AuditStudioId is left
// at its default (null) rather than adding a DB lookup to the marker interface's synchronous
// property. Known limitation: these entries currently log as platform-wide even though a
// referral code is conceptually studio-scoped. See architecture.md Decisions Log.
public record DeactivateReferralCodeCommand(Guid ReferralCodeId) : IRequest, IAuditableCommand
{
    public string AuditAction     => AuditActions.ReferralCodeDeactivated;
    public string AuditTargetType => AuditTargetTypes.ReferralCode;
    public Guid   AuditTargetId   => ReferralCodeId;
}

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
