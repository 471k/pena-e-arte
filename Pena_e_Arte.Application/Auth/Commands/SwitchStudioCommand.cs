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
    IAppDbContext                db,
    IIdentityService             identity,
    ICurrentUser                 currentUser,
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
            Client template = await db.FindAnyClientRecordForUserAsync(currentUser.UserId, ct)
                ?? throw new BusinessRuleViolationException("No existing client account found.");

            client = new Client
            {
                StudioId  = targetStudioId,
                UserId    = currentUser.UserId,
                FirstName = template.FirstName,
                LastName  = template.LastName,
                Email     = template.Email,
                Phone     = template.Phone,
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
