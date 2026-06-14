using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record UnsuspendStudioCommand(Guid StudioId) : IRequest;

public class UnsuspendStudioHandler(IAppDbContext db, ISubscriptionAccessService subscriptionAccess)
    : IRequestHandler<UnsuspendStudioCommand>
{
    public async Task Handle(UnsuspendStudioCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        studio.IsActive = true;
        await db.SaveChangesAsync(ct);
        await subscriptionAccess.InvalidateCacheAsync(command.StudioId, ct);
    }
}

public class UnsuspendStudioValidator : AbstractValidator<UnsuspendStudioCommand>
{
    public UnsuspendStudioValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
