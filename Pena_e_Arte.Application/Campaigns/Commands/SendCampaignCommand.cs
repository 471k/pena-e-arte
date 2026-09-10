using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Campaigns.Commands;

public record SendCampaignCommand(Guid CampaignId) : IRequest<CampaignResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.CampaignSent;
    public string AuditTargetType => AuditTargetTypes.Campaign;
    public Guid AuditTargetId => CampaignId;
}

public class SendCampaignHandler(IAppDbContext db, ICurrentTenant tenant, IJobScheduler jobs)
    : IRequestHandler<SendCampaignCommand, CampaignResponse>
{
    public async Task<CampaignResponse> Handle(SendCampaignCommand command, CancellationToken ct)
    {
        Campaign campaign = await db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == command.CampaignId, ct)
            ?? throw new NotFoundException(nameof(Campaign), command.CampaignId);

        if (campaign.Status != CampaignStatus.Draft)
            throw new BusinessRuleViolationException("Only a draft campaign can be sent.");

        // Unlike AllowApiAccess/PrioritySupport (hidden from UI/Help — sold-but-undelivered
        // flags with zero backing implementation), this flag IS actually enforced — see
        // Plan.AllowMarketingCampaigns's doc comment and architecture.md Decisions Log.
        Subscription? subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct);

        if (subscription?.Plan?.AllowMarketingCampaigns != true)
            throw new BusinessRuleViolationException(
                "Marketing campaigns aren't included on this studio's plan. Upgrade to send campaigns.");

        List<Client> audience = await db.ResolveCampaignAudienceAsync(campaign, ct);

        campaign.Status = CampaignStatus.Sending;
        campaign.RecipientCount = audience.Count;
        campaign.DeliveredCount = 0;
        await db.SaveChangesAsync(ct);

        jobs.EnqueueCampaignSend(campaign.Id);

        return CampaignMapper.Map(campaign);
    }
}

public class SendCampaignValidator : AbstractValidator<SendCampaignCommand>
{
    public SendCampaignValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty();
    }
}
