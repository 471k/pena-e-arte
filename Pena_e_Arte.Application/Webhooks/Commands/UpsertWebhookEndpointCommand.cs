using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Webhooks.Commands;

public record UpsertWebhookEndpointCommand(string Url) : IRequest<GenerateWebhookSecretResponse>;

/// <summary>
/// One active endpoint per studio, same "regenerate replaces" simplicity as
/// GenerateStudioApiKeyCommand — saving a new URL (or re-saving the same one) always
/// issues a fresh signing secret, since there's no way to show a previously-issued
/// secret again otherwise.
/// </summary>
public class UpsertWebhookEndpointHandler(IAppDbContext db, ICurrentTenant tenant, ITokenEncryptor encryptor)
    : IRequestHandler<UpsertWebhookEndpointCommand, GenerateWebhookSecretResponse>
{
    public async Task<GenerateWebhookSecretResponse> Handle(UpsertWebhookEndpointCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .Include(s => s.Subscription)
            .ThenInclude(sub => sub == null ? null : sub.Plan)
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

        bool planAllows = studio.Subscription?.Plan?.AllowApiAccess ?? false;
        if (!planAllows)
            throw new BusinessRuleViolationException(
                "Your current plan does not include API access.");

        WebhookEndpoint? existing = await db.WebhookEndpoints.FirstOrDefaultAsync(ct);

        string secret = WebhookSigner.GenerateSecret();
        string encryptedSecret = encryptor.Encrypt(secret);

        if (existing is null)
        {
            existing = new WebhookEndpoint { StudioId = tenant.StudioId };
            db.WebhookEndpoints.Add(existing);
        }

        existing.Url = command.Url;
        existing.EncryptedSecret = encryptedSecret;
        existing.IsActive = true;
        existing.ConsecutiveFailureCount = 0;
        existing.LastDeliveryAt = null;
        existing.LastDeliverySucceeded = null;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return new GenerateWebhookSecretResponse(existing.Url, secret, existing.CreatedAt);
    }
}

public class UpsertWebhookEndpointValidator : AbstractValidator<UpsertWebhookEndpointCommand>
{
    public UpsertWebhookEndpointValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(url => WebhookUrlValidator.IsAllowed(url))
            .WithMessage("The webhook URL must be a public HTTPS address.");
    }
}
