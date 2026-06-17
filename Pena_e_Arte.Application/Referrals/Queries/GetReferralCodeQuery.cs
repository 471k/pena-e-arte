using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Referrals.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Referrals.Queries;

public record GetReferralCodeQuery(Guid StudioId) : IRequest<ReferralCodeResponse?>;

public class GetReferralCodeHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetReferralCodeQuery, ReferralCodeResponse?>
{
    public async Task<ReferralCodeResponse?> Handle(GetReferralCodeQuery query, CancellationToken ct)
    {
        if (query.StudioId != tenant.StudioId)
            throw new NotFoundException(nameof(Studio), query.StudioId);

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
