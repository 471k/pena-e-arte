using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Public.Commands;

/// <summary>
/// Anonymous unsubscribe-link target — the signed token proves the clientId without a
/// session, mirroring the social OAuth callback signers' "prove identity without a
/// session" shape. Thin: only flips MarketingOptIn to false.
/// </summary>
public record WithdrawMarketingOptInCommand(string Token) : IRequest;

public class WithdrawMarketingOptInHandler(IAppDbContext db, IMarketingOptOutSigner signer)
    : IRequestHandler<WithdrawMarketingOptInCommand>
{
    public async Task Handle(WithdrawMarketingOptInCommand command, CancellationToken ct)
    {
        if (!signer.TryValidate(command.Token, out Guid clientId))
            throw new BusinessRuleViolationException("This unsubscribe link is invalid or has expired.");

        // IgnoreQueryFilters(): anonymous callback, no ambient tenant scope.
        Client? client = await db.Clients.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == clientId && c.DeletedAt == null, ct);

        if (client is null) return; // already erased/deleted — unsubscribe is a no-op, not an error

        client.MarketingOptIn = false;
        client.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

public class WithdrawMarketingOptInValidator : AbstractValidator<WithdrawMarketingOptInCommand>
{
    public WithdrawMarketingOptInValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
    }
}
