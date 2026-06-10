using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Studios.Commands;

public record UpdateStudioSlugCommand(Guid StudioId, string NewSlug) : IRequest<Unit>;

public class UpdateStudioSlugHandler(IAppDbContext db)
    : IRequestHandler<UpdateStudioSlugCommand, Unit>
{
    public async Task<Unit> Handle(UpdateStudioSlugCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        if (studio.SlugLockedAt.HasValue)
            throw new BusinessRuleViolationException("Studio slug has already been changed once and cannot be changed again.");

        bool taken = await db.Studios
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Slug == command.NewSlug && s.Id != command.StudioId, ct);

        if (taken)
            throw new BusinessRuleViolationException("This slug is already taken by another studio.");

        studio.Slug = command.NewSlug;
        studio.SlugLockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}

public class UpdateStudioSlugValidator : AbstractValidator<UpdateStudioSlugCommand>
{
    public UpdateStudioSlugValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.NewSlug)
            .NotEmpty()
            .MaximumLength(60)
            .Matches(@"^[a-z0-9-]+$")
            .WithMessage("Slug must contain only lowercase letters, numbers, and hyphens.");
    }
}
