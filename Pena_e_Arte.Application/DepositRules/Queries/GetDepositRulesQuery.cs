using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.DepositRules.Queries;

public record GetDepositRulesQuery : IRequest<List<DepositRuleResponse>>;

public class GetDepositRulesHandler(IAppDbContext db)
    : IRequestHandler<GetDepositRulesQuery, List<DepositRuleResponse>>
{
    public async Task<List<DepositRuleResponse>> Handle(GetDepositRulesQuery query, CancellationToken ct) =>
        await db.DepositRules
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.CreatedAt)
            .Select(r => CreateDepositRuleHandler.Map(r))
            .ToListAsync(ct);
}
