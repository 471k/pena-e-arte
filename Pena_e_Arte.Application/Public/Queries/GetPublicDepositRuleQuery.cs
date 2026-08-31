using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicDepositRuleQuery(string StudioSlug) : IRequest<PublicDepositRuleResponse?>;

public class GetPublicDepositRuleHandler(IAppDbContext db)
    : IRequestHandler<GetPublicDepositRuleQuery, PublicDepositRuleResponse?>
{
    public async Task<PublicDepositRuleResponse?> Handle(GetPublicDepositRuleQuery query, CancellationToken ct)
    {
        // Approved: public/anonymous studio-slug resolution — same predicate as GetPublicStudioHandler.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.StudioSlug && s.IsActive && s.IsPublished, ct)
            ?? throw new NotFoundException(nameof(Studio), query.StudioSlug);

        // Approved: public/anonymous — same "single active rule, if any" query
        // CreateAppointmentCoreAsync itself runs, IgnoreQueryFilters'd and scoped explicitly.
        DepositRule? rule = await db.DepositRules
            .IgnoreQueryFilters()
            .Where(r => r.StudioId == studio.Id && r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return rule is null ? null : new PublicDepositRuleResponse(rule.Name, rule.AmountFixed, rule.AmountPercent);
    }
}
