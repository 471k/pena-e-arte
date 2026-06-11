using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Referrals.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Referrals.Queries;

public record GetReferralCodeQuery(Guid StudioId) : IRequest<ReferralCodeResponse?>;

public class GetReferralCodeHandler(IAppDbContext db)
    : IRequestHandler<GetReferralCodeQuery, ReferralCodeResponse?>
{
    public async Task<ReferralCodeResponse?> Handle(GetReferralCodeQuery query, CancellationToken ct)
    {
        ReferralCode? code = await db.ReferralCodes
            .Where(r => r.StudioId == query.StudioId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return code is null ? null : GenerateReferralCodeHandler.Map(code);
    }
}

public class GetReferralCodeValidator : AbstractValidator<GetReferralCodeQuery>
{
    public GetReferralCodeValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
