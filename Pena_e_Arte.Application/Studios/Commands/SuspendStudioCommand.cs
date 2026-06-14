using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record SuspendStudioCommand(Guid StudioId) : IRequest;

public class SuspendStudioHandler(IAppDbContext db, ISubscriptionAccessService subscriptionAccess)
    : IRequestHandler<SuspendStudioCommand>
{
    public async Task Handle(SuspendStudioCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        studio.IsActive = false;
        await db.SaveChangesAsync(ct);
        await subscriptionAccess.InvalidateCacheAsync(command.StudioId, ct);
    }
}

public class SuspendStudioValidator : AbstractValidator<SuspendStudioCommand>
{
    public SuspendStudioValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
