using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

/// <summary>
/// POST /studios/me/hours/confirm (OwnerOnly): the owner says the opening hours registration set for
/// them are right (E05a, O02). Without this, an owner who agrees with the defaults could only stop
/// being told to check them by re-saving an unchanged form. It confirms hours that exist; it can't
/// silence a studio with no hours at all, which stays "not configured".
/// </summary>
public record ConfirmStudioHoursCommand : IRequest;

public class ConfirmStudioHoursValidator : AbstractValidator<ConfirmStudioHoursCommand>
{
    // No input: the studio comes from the caller's tenant.
}

public class ConfirmStudioHoursHandler(IAppDbContext db, ICurrentTenant tenant, TimeProvider time)
    : IRequestHandler<ConfirmStudioHoursCommand>
{
    public async Task Handle(ConfirmStudioHoursCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

        if (!await db.StudioHours.AnyAsync(ct))
            throw new BusinessRuleViolationException("Set your opening hours first.");

        // Idempotent: confirming again keeps the first confirmation rather than moving it.
        studio.HoursReviewedAt ??= time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}
