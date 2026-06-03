using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.DepositRules.Queries;

public record GetDepositRuleQuery(Guid Id) : IRequest<DepositRuleResponse>;

public class GetDepositRuleHandler(IAppDbContext db)
    : IRequestHandler<GetDepositRuleQuery, DepositRuleResponse>
{
    public async Task<DepositRuleResponse> Handle(GetDepositRuleQuery query, CancellationToken ct)
    {
        DepositRule? rule = await db.DepositRules
            .FirstOrDefaultAsync(r => r.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(DepositRule), query.Id);

        return CreateDepositRuleHandler.Map(rule);
    }
}
