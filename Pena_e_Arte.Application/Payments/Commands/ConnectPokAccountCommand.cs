using MediatR;
using Microsoft.EntityFrameworkCore;
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

public class ConnectPokAccountHandler(IAppDbContext db, ISecretsProvider secrets)
    : IRequestHandler<ConnectPokAccountCommand>
{
    public async Task Handle(ConnectPokAccountCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

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
        catch
        {
            studio.PokMerchantId = previousMerchantId;
            if (isNewCredentialRef)
                db.StudioCredentialRefs.Remove(credentialRef);
            await db.SaveChangesAsync(ct);
            throw;
        }
    }
}
