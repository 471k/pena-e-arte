using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record IssuerGenerateReferralCodeCommand(Guid StudioId) : IRequest<PlatformReferralCodeResponse>;

public class IssuerGenerateReferralCodeHandler(
    IAppDbContext                                  db,
    ILogger<IssuerGenerateReferralCodeHandler>     logger)
    : IRequestHandler<IssuerGenerateReferralCodeCommand, PlatformReferralCodeResponse>
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task<PlatformReferralCodeResponse> Handle(
        IssuerGenerateReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #9 — issuer generates referral code
        // for any studio cross-tenant. See architecture.md.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        List<ReferralCode> existing = await db.ReferralCodes
            .IgnoreQueryFilters()
            .Where(r => r.StudioId == command.StudioId && r.IsActive)
            .ToListAsync(ct);
        foreach (ReferralCode old in existing)
            old.IsActive = false;

        string code = await GenerateUniqueCodeAsync(ct);

        ReferralCode referralCode = new()
        {
            StudioId    = command.StudioId,
            Code        = code,
            IsActive    = true,
            IsSingleUse = true,
        };

        db.ReferralCodes.Add(referralCode);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Issuer generated referral code {ReferralCodeId} for studio {StudioId}",
            referralCode.Id, command.StudioId);

        return new PlatformReferralCodeResponse(
            referralCode.Id,
            referralCode.StudioId,
            studio.Name,
            referralCode.Code,
            referralCode.IsActive,
            referralCode.IsSingleUse,
            referralCode.CreatedAt,
            referralCode.ExpiresAt,
            RedemptionCount: 0);
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string candidate = GenerateCode();
            bool taken = await db.ReferralCodes
                .IgnoreQueryFilters()
                .AnyAsync(r => r.Code == candidate, ct);
            if (!taken) return candidate;
        }
        throw new InvalidOperationException(
            "Unable to generate a unique referral code after 10 attempts.");
    }

    internal static string GenerateCode()
    {
        char[] chars = new char[8];
        byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        for (int i = 0; i < 8; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }
}

public class IssuerGenerateReferralCodeValidator
    : AbstractValidator<IssuerGenerateReferralCodeCommand>
{
    public IssuerGenerateReferralCodeValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
