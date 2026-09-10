using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ClientReferrals.Commands;

/// <summary>
/// Idempotent: returns the caller's existing ClientReferralCode for this studio if one
/// exists, otherwise generates one. A client has at most one active referral code per
/// studio — enforced by the (StudioId, ReferrerClientId) unique index.
/// </summary>
public record GetOrCreateMyReferralCodeCommand : IRequest<ClientReferralCodeResponse>;

public class GetOrCreateMyReferralCodeHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetOrCreateMyReferralCodeCommand, ClientReferralCodeResponse>
{
    /// <summary>Flat percent off the next deposit — both referrer and referee. No per-code
    /// choice (unlike PromoCode's AmountFixed/AmountPercent split); this is a fixed platform
    /// mechanic, not studio-configurable.</summary>
    internal const decimal RewardPercent = 10m;

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task<ClientReferralCodeResponse> Handle(GetOrCreateMyReferralCodeCommand command, CancellationToken ct)
    {
        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        Studio studio = await db.Studios.FirstAsync(s => s.Id == client.StudioId, ct);

        ClientReferralCode? existing = await db.ClientReferralCodes
            .FirstOrDefaultAsync(c => c.ReferrerClientId == client.Id, ct);

        if (existing is not null) return Map(existing, studio.Slug);

        string code = await GenerateUniqueCodeAsync(client.StudioId, ct);

        ClientReferralCode referralCode = new()
        {
            StudioId = client.StudioId,
            ReferrerClientId = client.Id,
            Code = code,
            RewardPercent = RewardPercent,
            RedemptionCount = 0,
        };

        db.ClientReferralCodes.Add(referralCode);
        await db.SaveChangesAsync(ct);

        return Map(referralCode, studio.Slug);
    }

    private async Task<string> GenerateUniqueCodeAsync(Guid studioId, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string candidate = GenerateCode();
            bool taken = await db.ClientReferralCodes
                .IgnoreQueryFilters()
                .AnyAsync(c => c.StudioId == studioId && c.Code == candidate, ct);
            if (!taken) return candidate;
        }
        throw new InvalidOperationException("Unable to generate a unique client referral code after 10 attempts.");
    }

    internal static string GenerateCode()
    {
        char[] chars = new char[8];
        byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        for (int i = 0; i < 8; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    internal static ClientReferralCodeResponse Map(ClientReferralCode c, string studioSlug) => new(
        c.Id,
        c.Code,
        $"https://tattooos.co/book?studio={studioSlug}&referral={c.Code}",
        c.RewardPercent,
        c.RedemptionCount);
}
