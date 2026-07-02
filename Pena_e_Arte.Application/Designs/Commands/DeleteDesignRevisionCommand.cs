using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record DeleteDesignRevisionCommand(Guid DesignId, Guid RevisionId) : IRequest;

public class DeleteDesignRevisionHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DeleteDesignRevisionCommand>
{
    public async Task Handle(DeleteDesignRevisionCommand command, CancellationToken ct)
    {
        DesignRevision revision = await db.DesignRevisions
            .FirstOrDefaultAsync(r => r.DesignId == command.DesignId && r.Id == command.RevisionId, ct)
            ?? throw new NotFoundException(nameof(DesignRevision), command.RevisionId);

        if (currentUser.Role == "artist")
        {
            bool ownsDesign = await db.Designs
                .Join(db.Artists, d => d.ArtistId, a => a.Id, (d, a) => new { d.Id, a.UserId })
                .AnyAsync(x => x.Id == command.DesignId && x.UserId == currentUser.UserId, ct);
            if (!ownsDesign) throw new ForbiddenException();
        }

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
