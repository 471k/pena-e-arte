using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Designs.Commands;

public record DeleteDesignRevisionCommand(Guid DesignId, Guid RevisionId) : IRequest;

public class DeleteDesignRevisionHandler(IAppDbContext db)
    : IRequestHandler<DeleteDesignRevisionCommand>
{
    public async Task Handle(DeleteDesignRevisionCommand command, CancellationToken ct)
    {
        DesignRevision revision = await db.DesignRevisions
            .FirstOrDefaultAsync(r => r.DesignId == command.DesignId && r.Id == command.RevisionId, ct)
            ?? throw new NotFoundException(nameof(DesignRevision), command.RevisionId);

        db.DesignRevisions.Remove(revision);
        await db.SaveChangesAsync(ct);
    }
}

public class DeleteDesignRevisionValidator : AbstractValidator<DeleteDesignRevisionCommand>
{
    public DeleteDesignRevisionValidator()
    {
        RuleFor(x => x.DesignId).NotEmpty();
        RuleFor(x => x.RevisionId).NotEmpty();
    }
}
