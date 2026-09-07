using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

public record AdminGenerateReferralCodeCommand(Guid StudioId, DateTime? ExpiresAt = null) : IRequest<PlatformReferralCodeResponse>;

public class AdminGenerateReferralCodeHandler(
    IAppDbContext db,
    IRealtimeNotifier realtime,
    ILogger<AdminGenerateReferralCodeHandler> logger)
    : IRequestHandler<AdminGenerateReferralCodeCommand, PlatformReferralCodeResponse>
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task<PlatformReferralCodeResponse> Handle(
        AdminGenerateReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #9 — admin generates referral code
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
            StudioId = command.StudioId,
            Code = code,
            IsActive = true,
            IsSingleUse = true,
            ExpiresAt = command.ExpiresAt,
        };

        db.ReferralCodes.Add(referralCode);

        string shareUrl = $"https://tattooos.co/register?ref={referralCode.Code}";

        // In-app only — no email/SMS equivalent, and not gated behind the studio's
        // per-event notification preferences (those only cover Email/Sms channels).
        NotificationLog notice = new()
        {
            StudioId = studio.Id,
            RecipientId = studio.Id,
            RecipientType = NotificationRecipientType.Studio,
            Channel = NotificationChannel.InApp,
            Subject = "A referral code was generated for your studio",
            Body = $"The TattooOS team generated referral code {referralCode.Code} for your studio. " +
                             $"Share it with other studio owners — new studios that sign up with it get one month free, and so do you: {shareUrl}",
            SentAt = DateTime.UtcNow,
            IsSuccess = true,
        };
        db.NotificationLogs.Add(notice);

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            studio.Id, "NotificationReceived",
            GetNotificationsHandler.Map(notice, studio.Name), ct);

        logger.LogInformation(
            "Admin generated referral code {ReferralCodeId} for studio {StudioId}",
            referralCode.Id, command.StudioId);

        return new PlatformReferralCodeResponse(
            referralCode.Id,
            referralCode.StudioId,
            studio.Name,
            referralCode.Code,
            shareUrl,
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

public class AdminGenerateReferralCodeValidator
    : AbstractValidator<AdminGenerateReferralCodeCommand>
{
    public AdminGenerateReferralCodeValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
