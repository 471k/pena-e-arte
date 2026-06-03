using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.DepositRules.Commands;

public record UpdateDepositRuleCommand(Guid Id, UpdateDepositRuleRequest Request) : IRequest<DepositRuleResponse>;

public class UpdateDepositRuleHandler(IAppDbContext db)
    : IRequestHandler<UpdateDepositRuleCommand, DepositRuleResponse>
{
    public async Task<DepositRuleResponse> Handle(UpdateDepositRuleCommand command, CancellationToken ct)
    {
        UpdateDepositRuleRequest req = command.Request;

        DepositRule? rule = await db.DepositRules
            .FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(DepositRule), command.Id);

        if (req.IsActive && !rule.IsActive)
        {
            List<DepositRule> others = await db.DepositRules
                .Where(r => r.IsActive && r.Id != command.Id)
                .ToListAsync(ct);

            foreach (DepositRule other in others)
            {
                other.IsActive  = false;
                other.UpdatedAt = DateTime.UtcNow;
            }
        }

        rule.Name          = req.Name;
        rule.AmountFixed   = req.AmountFixed;
        rule.AmountPercent = req.AmountPercent;
        rule.IsActive      = req.IsActive;
        rule.UpdatedAt     = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return CreateDepositRuleHandler.Map(rule);
    }
}
