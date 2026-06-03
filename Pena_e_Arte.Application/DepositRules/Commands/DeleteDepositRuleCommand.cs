using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.DepositRules.Commands;

public record DeleteDepositRuleCommand(Guid Id) : IRequest;

public class DeleteDepositRuleHandler(IAppDbContext db)
    : IRequestHandler<DeleteDepositRuleCommand>
{
    public async Task Handle(DeleteDepositRuleCommand command, CancellationToken ct)
    {
        DepositRule? rule = await db.DepositRules
            .FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(DepositRule), command.Id);

        rule.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
