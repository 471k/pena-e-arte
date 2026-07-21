using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record AddStudioClosureCommand(
    Guid     StudioId,
    DateTime StartDate,
    DateTime EndDate,
    string   Reason) : IRequest<Guid>;

public class AddStudioClosureValidator : AbstractValidator<AddStudioClosureCommand>
{
    public AddStudioClosureValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class AddStudioClosureHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<AddStudioClosureCommand, Guid>
{
    public async Task<Guid> Handle(AddStudioClosureCommand command, CancellationToken ct)
    {
        if (command.StudioId != tenant.StudioId)
            throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        DateTime startDate = command.StartDate.Date;
        DateTime endDate   = command.EndDate.Date;

        bool overlaps = await db.StudioClosures.AnyAsync(c =>
            c.StartDate <= endDate
            && c.EndDate >= startDate, ct);

        if (overlaps)
            throw new BusinessRuleViolationException(
                "This closure period overlaps with an existing one.");

        StudioClosure closure = new()
        {
            StudioId  = tenant.StudioId,
            StartDate = startDate,
            EndDate   = endDate,
            Reason    = command.Reason,
        };

        db.StudioClosures.Add(closure);
        await db.SaveChangesAsync(ct);
        return closure.Id;
    }
}
