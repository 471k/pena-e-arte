using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record DeleteStudioClosureCommand(Guid StudioId, Guid ClosureId) : IRequest;

public class DeleteStudioClosureHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<DeleteStudioClosureCommand>
{
    public async Task Handle(DeleteStudioClosureCommand command, CancellationToken ct)
    {
        if (command.StudioId != tenant.StudioId)
            throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        var closure = await db.StudioClosures
            .FirstOrDefaultAsync(c => c.Id == command.ClosureId, ct)
            ?? throw new NotFoundException("StudioClosure", command.ClosureId);

        closure.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
