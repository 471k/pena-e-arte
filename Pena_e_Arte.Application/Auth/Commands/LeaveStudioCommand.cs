using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record LeaveStudioCommand(Guid StudioId) : IRequest<LeaveStudioResponse>;

public class LeaveStudioHandler(
    IIdentityService identity,
    ICurrentUser currentUser,
    ILogger<LeaveStudioHandler> logger)
    : IRequestHandler<LeaveStudioCommand, LeaveStudioResponse>
{
    public async Task<LeaveStudioResponse> Handle(
        LeaveStudioCommand command, CancellationToken ct)
    {
        IReadOnlyList<Guid> tenantIds =
            await identity.GetTenantIdsAsync(currentUser.UserId, ct);

        if (!tenantIds.Contains(command.StudioId))
            throw new NotFoundException("Studio membership", command.StudioId);

        Guid? activeTenantId = await identity.GetActiveTenantIdAsync(currentUser.UserId, ct);
        bool isLeavingActiveTenant = activeTenantId == command.StudioId;

        await identity.RemoveTenantClaimAsync(currentUser.UserId, command.StudioId, ct);

        logger.LogInformation(
            "Client {@UserId} left studio {@StudioId} (was active tenant: {@WasActive})",
            currentUser.UserId, command.StudioId, isLeavingActiveTenant);

        return new LeaveStudioResponse(isLeavingActiveTenant);
    }
}

public class LeaveStudioValidator : AbstractValidator<LeaveStudioCommand>
{
    public LeaveStudioValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
