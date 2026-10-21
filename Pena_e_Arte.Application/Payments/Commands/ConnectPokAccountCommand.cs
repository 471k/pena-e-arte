using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

/// <summary>
/// Owner connects their studio's own POK merchant account (ADR-0001 — there is no platform-level
/// key; every studio brings its own keyId/keySecret from its POK dashboard). keyId/keySecret are
/// written straight to Vault via ISecretsProvider and never touch MySQL; only the routing
/// merchantId lands on Studio. This is the piece PokPaymentProvider had no way to be reached
/// through before this command existed.
/// </summary>
public record ConnectPokAccountCommand(Guid StudioId, ConnectPokAccountRequest Request) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.PokAccountConnected;
    public string AuditTargetType => AuditTargetTypes.Studio;
    public Guid AuditTargetId => StudioId;
}

public class ConnectPokAccountHandler(
    IAppDbContext db, ISecretsProvider secrets, IPokCredentialVerifier verifier, ILogger<ConnectPokAccountHandler> logger)
    : IRequestHandler<ConnectPokAccountCommand>
{
    /// <summary>What the owner sees when the keys can't be stored because the secrets store is
    /// down (C-8) — was a bare 500 before.</summary>
    public const string SecretsStoreUnavailableMessage =
        "We couldn't save your POK keys because our secure key store is temporarily unavailable. Nothing was changed — please try again in a few minutes.";

    /// <summary>C-7: POK refused the Key ID + Key Secret. Before, wrong keys were saved silently and
    /// only failed at the studio's first card checkout.</summary>
    public const string InvalidKeysMessage =
        "POK didn't accept this Key ID and Key Secret. Copy both again from POK Business → E-payments → API Keys and try again. Nothing was saved.";

    /// <summary>C-7: the keys are fine, but POK forbids them for this merchant id (staging answers 403
    /// "Access Forbidden!" for a merchant the keys don't belong to).</summary>
    public const string MerchantMismatchMessage =
        "POK accepted your keys, but this Merchant ID doesn't belong to them. Copy the Merchant ID from the same API key window in POK Business and try again. Nothing was saved.";

    /// <summary>C-7: POK couldn't be asked — we never save keys we haven't been able to check.</summary>
    public const string PokUnreachableMessage =
        "We couldn't reach POK to check your keys. Nothing was saved — please try again in a few minutes.";

    public async Task Handle(ConnectPokAccountCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        // Test the keys and the merchant id before anything is written, so a refusal leaves the studio
        // exactly as it was (including an existing, working connection when the owner is replacing keys).
        PokCredentialCheckResult check = await verifier.CheckAsync(
            command.Request.KeyId, command.Request.KeySecret, command.Request.MerchantId, ct);
        switch (check)
        {
            case PokCredentialCheckResult.InvalidKeys:
                throw new BusinessRuleViolationException(InvalidKeysMessage);
            case PokCredentialCheckResult.MerchantMismatch:
                throw new BusinessRuleViolationException(MerchantMismatchMessage);
            case PokCredentialCheckResult.PokUnreachable:
                throw new ServiceUnavailableException(PokUnreachableMessage);
        }

        StudioCredentialRef? credentialRef = await db.StudioCredentialRefs
            .FirstOrDefaultAsync(c => c.StudioId == command.StudioId && c.Provider == CredentialProvider.Pok, ct);

        string secretPath = $"studios/{command.StudioId}/pok";
        string? previousMerchantId = studio.PokMerchantId;
        bool isNewCredentialRef = credentialRef is null;

        if (credentialRef is null)
        {
            credentialRef = new StudioCredentialRef
            {
                StudioId = command.StudioId,
                Provider = CredentialProvider.Pok,
                SecretPath = secretPath,
            };
            db.StudioCredentialRefs.Add(credentialRef);
        }

        studio.PokMerchantId = command.Request.MerchantId;
        await db.SaveChangesAsync(ct);

        // MySQL commits first so a Vault failure can be compensated by reverting it below. The
        // reverse order risked leaving Vault holding rotated credentials while MySQL still
        // pointed at the old merchantId — a live mismatched credential pair, not just "looks
        // disconnected."
        try
        {
            await secrets.SetSecretAsync(secretPath, new Dictionary<string, string>
            {
                ["keyId"] = command.Request.KeyId,
                ["keySecret"] = command.Request.KeySecret,
            }, ct);
        }
        catch (Exception ex)
        {
            studio.PokMerchantId = previousMerchantId;
            if (isNewCredentialRef)
                db.StudioCredentialRefs.Remove(credentialRef);
            await db.SaveChangesAsync(ct);

            if (ex is SecretsStoreUnavailableException)
            {
                logger.LogError(
                    "Secrets store unavailable while connecting POK for studio {StudioId}; the connection was reverted.",
                    command.StudioId);
                throw new ServiceUnavailableException(SecretsStoreUnavailableMessage);
            }

            throw;
        }
    }
}
