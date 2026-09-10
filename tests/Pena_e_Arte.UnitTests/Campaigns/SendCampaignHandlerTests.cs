using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Campaigns.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Campaigns;

public class SendCampaignHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();

    public SendCampaignHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private SendCampaignHandler CreateSut() => new(_db, _tenant, _jobs);

    private Campaign SeedDraftCampaign()
    {
        Campaign campaign = new()
        {
            StudioId = _studioId,
            Subject = "Hello",
            BodyHtml = "<p>Hi</p>",
            Audience = CampaignAudience.AllClients,
            Status = CampaignStatus.Draft,
        };
        _db.Campaigns.Add(campaign);
        _db.SaveChanges();
        return campaign;
    }

    private void SeedSubscription(bool allowMarketingCampaigns)
    {
        Plan plan = new() { Name = "Test", AllowMarketingCampaigns = allowMarketingCampaigns };
        _db.Plans.Add(plan);
        _db.Subscriptions.Add(new Subscription { StudioId = _studioId, PlanId = plan.Id, Plan = plan });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Handle_PlanDoesNotAllowMarketingCampaigns_ThrowsBusinessRuleViolationException()
    {
        SeedSubscription(allowMarketingCampaigns: false);
        Campaign campaign = SeedDraftCampaign();

        Func<Task> act = () => CreateSut().Handle(new SendCampaignCommand(campaign.Id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        _jobs.DidNotReceiveWithAnyArgs().EnqueueCampaignSend(default);
    }

    [Fact]
    public async Task Handle_NoSubscription_ThrowsBusinessRuleViolationException()
    {
        Campaign campaign = SeedDraftCampaign();

        Func<Task> act = () => CreateSut().Handle(new SendCampaignCommand(campaign.Id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PlanAllowsMarketingCampaigns_TransitionsToSendingAndEnqueuesJob()
    {
        SeedSubscription(allowMarketingCampaigns: true);
        Campaign campaign = SeedDraftCampaign();

        CampaignResponse result = await CreateSut().Handle(new SendCampaignCommand(campaign.Id), default);

        result.Status.Should().Be(CampaignStatus.Sending.ToString());
        _jobs.Received(1).EnqueueCampaignSend(campaign.Id);
    }

    [Fact]
    public async Task Handle_AlreadySentCampaign_ThrowsBusinessRuleViolationException()
    {
        SeedSubscription(allowMarketingCampaigns: true);
        Campaign campaign = SeedDraftCampaign();
        campaign.Status = CampaignStatus.Sent;
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new SendCampaignCommand(campaign.Id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }
}
