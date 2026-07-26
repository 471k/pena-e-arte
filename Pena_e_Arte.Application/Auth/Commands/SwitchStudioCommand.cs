using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record SwitchStudioCommand(SwitchStudioRequest Request) : IRequest<SwitchStudioResponse>;

public class SwitchStudioHandler(
    IAppDbContext db,
    IIdentityService identity,
    ICurrentUser currentUser,
    ILogger<SwitchStudioHandler> logger)
    : IRequestHandler<SwitchStudioCommand, SwitchStudioResponse>
{
    public async Task<SwitchStudioResponse> Handle(SwitchStudioCommand command, CancellationToken ct)
    {
        Guid targetStudioId = command.Request.StudioId;

        // Studio is not itself tenant-scoped (it IS the tenant) — no filter to bypass.
        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == targetStudioId, ct);
        if (studio is null) throw new NotFoundException(nameof(Studio), targetStudioId);

        Client? client = await db.FindClientForUserAtStudioAsync(currentUser.UserId, targetStudioId, ct);
        bool isNewMembership = client is null;

        if (isNewMembership)
        {
            // A studio-less registrant has no prior Client row anywhere — this is
            // their first-ever studio membership, so seed from their own account
            // instead of a template (which template lookup correctly returns null for).
            Client? template = await db.FindAnyClientRecordForUserAsync(currentUser.UserId, ct);

            string email = template?.Email ?? currentUser.Email
                ?? throw new BusinessRuleViolationException("Could not determine an email for this account.");
            string firstName = template?.FirstName ?? email.Split('@')[0];
            string lastName = template?.LastName ?? string.Empty;
            string? phone = template?.Phone;

            client = new Client
            {
                StudioId = targetStudioId,
                UserId = currentUser.UserId,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Phone = phone,
            };
            db.Clients.Add(client);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost a race with a concurrent switch/registration for the same
                // (user, studio) pair — the row now exists under the unique
                // (StudioId, Email) index, so recover by re-fetching instead of
                // failing the request.
                client = await db.FindClientForUserAtStudioAsync(currentUser.UserId, targetStudioId, ct);
                if (client is null) throw;
            }
        }

        await identity.EnsureTenantClaimAsync(currentUser.UserId, targetStudioId, ct);

        (bool success, string? accessToken, string? refreshToken, string? error) =
            await identity.IssueTokensForTenantAsync(currentUser.UserId, targetStudioId, ct);

        if (!success) throw new BusinessRuleViolationException(error ?? "Could not switch studio.");

        logger.LogInformation(
            "Client {@UserId} switched active studio to {@StudioId} (new membership: {@IsNewMembership})",
            currentUser.UserId, targetStudioId, isNewMembership);

        return new SwitchStudioResponse(accessToken!, refreshToken!, isNewMembership);
    }
}

public class SwitchStudioValidator : AbstractValidator<SwitchStudioCommand>
{
    public SwitchStudioValidator()
    {
        RuleFor(x => x.Request.StudioId).NotEmpty();
    }
}
