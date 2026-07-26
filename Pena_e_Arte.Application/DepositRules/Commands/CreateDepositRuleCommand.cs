using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.DepositRules.Commands;

public record CreateDepositRuleCommand(CreateDepositRuleRequest Request) : IRequest<DepositRuleResponse>;

public class CreateDepositRuleHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateDepositRuleCommand, DepositRuleResponse>
{
    public async Task<DepositRuleResponse> Handle(CreateDepositRuleCommand command, CancellationToken ct)
    {
        CreateDepositRuleRequest req = command.Request;

        if (req.IsActive)
            await DeactivateAllAsync(ct);

        DepositRule rule = new()
        {
            StudioId = tenant.StudioId,
            Name = req.Name,
            AmountFixed = req.AmountFixed,
            AmountPercent = req.AmountPercent,
            IsActive = req.IsActive,
            CancellationWindowHours = req.CancellationWindowHours,
            RefundPercentOnLateCancel = req.RefundPercentOnLateCancel
        };

        db.DepositRules.Add(rule);
        await db.SaveChangesAsync(ct);

        return Map(rule);
    }

    private async Task DeactivateAllAsync(CancellationToken ct)
    {
        List<DepositRule> active = await db.DepositRules
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        foreach (DepositRule r in active)
        {
            r.IsActive = false;
            r.UpdatedAt = DateTime.UtcNow;
        }
    }

    internal static DepositRuleResponse Map(DepositRule r) => new(
        r.Id, r.StudioId, r.Name,
        r.AmountFixed, r.AmountPercent,
        r.IsActive, r.CreatedAt, r.UpdatedAt,
        r.CancellationWindowHours, r.RefundPercentOnLateCancel);
}
