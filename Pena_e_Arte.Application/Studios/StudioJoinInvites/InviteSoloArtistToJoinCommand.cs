using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.StudioJoinInvites;

public record InviteSoloArtistToJoinCommand(CreateArtistRequest Request) : IRequest<StudioJoinInviteResponse>;

public class InviteSoloArtistToJoinValidator : AbstractValidator<InviteSoloArtistToJoinCommand>
{
    public InviteSoloArtistToJoinValidator()
    {
        // Mirrors CreateArtistValidator exactly — same CreateArtistRequest shape, and this
        // invite's Specializations/HourlyRate are copied verbatim onto the real Artist row at
        // accept time (AcceptStudioJoinInviteCommand), so the same bounds must apply here too.
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.HourlyRate).InclusiveBetween(0.01m, 10_000m)
            .When(x => x.Request.HourlyRate is not null);
        RuleFor(x => x.Request.Specializations).MaximumLength(1000)
            .When(x => x.Request.Specializations is not null);
    }
}

public class InviteSoloArtistToJoinHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IIdentityService identity,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    IAppSettings appSettings,
    ILogger<InviteSoloArtistToJoinHandler> logger)
    : IRequestHandler<InviteSoloArtistToJoinCommand, StudioJoinInviteResponse>
{
    public async Task<StudioJoinInviteResponse> Handle(InviteSoloArtistToJoinCommand command, CancellationToken ct)
    {
        CreateArtistRequest req = command.Request;

        // Resolve the invited email to an existing owner-of-a-solo-studio account — the only
        // account shape this invite type is meant for. Anything else is rejected with a clear
        // message pointing back to the normal CreateArtistCommand flow.
        Guid? existingUserId = await identity.GetUserIdByEmailAsync(req.Email, ct);
        if (existingUserId is null)
            throw new BusinessRuleViolationException(
                $"No account exists for '{req.Email}'. To invite someone who doesn't have an account yet, use the normal artist invite instead.");

        IReadOnlyList<string> existingRoles = await identity.GetUserRolesAsync(existingUserId.Value, ct);
        if (!existingRoles.Contains("owner"))
            throw new BusinessRuleViolationException(
                $"The email '{req.Email}' does not belong to an independent solo artist's account.");

        // IgnoreQueryFilters: resolving another tenant's studio by owner email is exactly what
        // this cross-studio invite flow requires — see AppDbContext's StudioJoinInvite comment.
        Studio? soloStudio = await db.Studios.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OwnerEmail == req.Email && s.IsSolo && s.IsActive, ct);

        if (soloStudio is null)
            throw new BusinessRuleViolationException(
                $"The email '{req.Email}' does not belong to an independent solo artist's account. Use the normal artist invite instead.");

        bool alreadyPending = await db.StudioJoinInvites.AnyAsync(
            i => i.StudioId == tenant.StudioId
                 && i.InvitedEmail == req.Email
                 && i.Status == StudioJoinInviteStatus.Pending, ct);

        if (alreadyPending)
            throw new BusinessRuleViolationException(
                $"There is already a pending invite for '{req.Email}' at this studio.");

        StudioJoinInvite invite = new()
        {
            StudioId = tenant.StudioId,
            InvitedEmail = req.Email,
            FirstName = req.FirstName,
            LastName = req.LastName,
            Specializations = req.Specializations,
            HourlyRate = req.HourlyRate,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        db.StudioJoinInvites.Add(invite);
        await db.SaveChangesAsync(ct);

        // Non-fatal: the invite exists and is visible to the invitee either way once they log in.
        try
        {
            Studio invitingStudio = await db.Studios.FirstAsync(s => s.Id == tenant.StudioId, ct);
            string body = emailRenderer.RenderStudioJoinInvite(
                invitingStudio.Name, invitingStudio.City, $"{appSettings.BaseUrl}/login");
            await notifications.SendEmailAsync(
                req.Email, $"{invitingStudio.Name} wants you to join", body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send studio-join-invite email for invite {@InviteId}", invite.Id);
        }

        logger.LogInformation(
            "Studio {@StudioId} invited solo artist account {@InvitedUserId} to join via invite {@InviteId}",
            tenant.StudioId, existingUserId, invite.Id);

        return new StudioJoinInviteResponse(
            invite.Id, invite.InvitedEmail, invite.Status.ToString(), invite.ExpiresAt);
    }
}
