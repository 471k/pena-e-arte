using FluentValidation;
using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Campaigns.Commands;

/// <summary>Creates a Draft campaign — sending is a separate step (SendCampaignCommand).</summary>
public record CreateCampaignCommand(CreateCampaignRequest Request) : IRequest<CampaignResponse>;

public class CreateCampaignHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateCampaignCommand, CampaignResponse>
{
    public async Task<CampaignResponse> Handle(CreateCampaignCommand command, CancellationToken ct)
    {
        CreateCampaignRequest req = command.Request;

        Campaign campaign = new()
        {
            StudioId = tenant.StudioId,
            Subject = req.Subject,
            BodyHtml = req.BodyHtml,
            Audience = Enum.Parse<CampaignAudience>(req.Audience),
            Status = CampaignStatus.Draft,
            NoRecentVisitDays = req.Audience == nameof(CampaignAudience.ClientsWithNoRecentVisit)
                ? req.NoRecentVisitDays ?? 90
                : null,
            CustomClientIds = req.Audience == nameof(CampaignAudience.Custom)
                ? req.CustomClientIds?.ToList() ?? []
                : [],
        };

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return CampaignMapper.Map(campaign);
    }
}

public class CreateCampaignValidator : AbstractValidator<CreateCampaignCommand>
{
    private static readonly string[] ValidAudiences =
        Enum.GetNames<CampaignAudience>();

    public CreateCampaignValidator()
    {
        RuleFor(x => x.Request.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.BodyHtml).NotEmpty();
        RuleFor(x => x.Request.Audience)
            .Must(a => ValidAudiences.Contains(a))
            .WithMessage("Audience must be one of: " + string.Join(", ", ValidAudiences));
        RuleFor(x => x.Request.NoRecentVisitDays)
            .GreaterThan(0)
            .When(x => x.Request.NoRecentVisitDays is not null);
        RuleFor(x => x.Request.CustomClientIds)
            .NotEmpty()
            .WithMessage("Select at least one client for a custom audience.")
            .When(x => x.Request.Audience == nameof(CampaignAudience.Custom));
    }
}
