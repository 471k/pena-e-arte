using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Referrals.Commands;

public record GenerateReferralCodeCommand(Guid StudioId) : IRequest<ReferralCodeResponse>;

public class GenerateReferralCodeHandler(
    IAppDbContext                          db,
    ICurrentTenant                         tenant,
    ILogger<GenerateReferralCodeHandler>   logger)
    : IRequestHandler<GenerateReferralCodeCommand, ReferralCodeResponse>
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task<ReferralCodeResponse> Handle(GenerateReferralCodeCommand command, CancellationToken ct)
    {
        if (command.StudioId != tenant.StudioId)
            throw new NotFoundException(nameof(Studio), command.StudioId);

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        // Deactivate any existing active codes before generating a new one
        List<ReferralCode> existing = await db.ReferralCodes
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

        logger.LogInformation("Referral code generated {@ReferralCodeId} for studio {@StudioId}",
            referralCode.Id, command.StudioId);

        return Map(referralCode);
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string candidate = GenerateCode();
            bool taken = await db.ReferralCodes.AnyAsync(r => r.Code == candidate, ct);
            if (!taken) return candidate;
        }
        throw new InvalidOperationException("Unable to generate a unique referral code after 10 attempts.");
    }

    internal static string GenerateCode()
    {
        char[] chars = new char[8];
        byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        for (int i = 0; i < 8; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    internal static ReferralCodeResponse Map(ReferralCode r) => new(
        r.Id,
        r.Code,
        $"https://penaearte.com/register?ref={r.Code}",
        r.IsActive,
        r.IsSingleUse,
        r.CreatedAt,
        r.ExpiresAt);
}

public class GenerateReferralCodeValidator : AbstractValidator<GenerateReferralCodeCommand>
{
    public GenerateReferralCodeValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
