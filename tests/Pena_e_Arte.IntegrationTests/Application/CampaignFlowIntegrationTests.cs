using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Campaigns.Commands;
using Pena_e_Arte.Application.Campaigns.Queries;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class CampaignFlowIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task FullSendFlow_CreateDraftThenSend_JobProcessesAudienceAndUpdatesCampaign()
    {
        Guid studioId = await SeedStudio();
        await SeedActiveSubscription(studioId, allowMarketingCampaigns: true);
        Client optedIn1 = await SeedClient(studioId, marketingOptIn: true);
        Client optedIn2 = await SeedClient(studioId, marketingOptIn: true);
        await SeedClient(studioId, marketingOptIn: false);

        // 1. Owner creates a draft.
        CampaignResponse draft = await RunCreateCampaign(studioId, new CreateCampaignRequest(
            "Spring Sale", "<p>Hello</p>", nameof(CampaignAudience.AllClients)));
        draft.Status.Should().Be(CampaignStatus.Draft.ToString());

        // 2. Owner sends it.
        IJobScheduler jobs = Substitute.For<IJobScheduler>();
        CampaignResponse sent = await RunSendCampaign(studioId, draft.Id, jobs);
        sent.Status.Should().Be(CampaignStatus.Sending.ToString());
        sent.RecipientCount.Should().Be(2);

        // 3. SendCampaignJob processes the audience (invoked directly, as the enqueued job would be).
        INotificationService notifications = Substitute.For<INotificationService>();
        IMarketingOptOutSigner signer = Substitute.For<IMarketingOptOutSigner>();
        signer.Sign(Arg.Any<Guid>()).Returns("signed-token");

        await using AppDbContext jobDb = fixture.CreateDbContext(studioId);
        SendCampaignJob job = new(jobDb, notifications, signer, NullLogger<SendCampaignJob>.Instance);
        await job.RunAsync(draft.Id);

        await notifications.Received(2).SendEmailAsync(
            Arg.Is<string>(to => to == optedIn1.Email || to == optedIn2.Email),
            "Spring Sale", Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        Campaign? finalCampaign = await verify.Campaigns.FirstOrDefaultAsync(c => c.Id == draft.Id);
        finalCampaign!.Status.Should().Be(CampaignStatus.Sent);
        finalCampaign.DeliveredCount.Should().Be(2);
        finalCampaign.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Unsubscribe_FlipsMarketingOptIn_SecondSendExcludesThatClient()
    {
        Guid studioId = await SeedStudio();
        await SeedActiveSubscription(studioId, allowMarketingCampaigns: true);
        Client client = await SeedClient(studioId, marketingOptIn: true);

        // 1. Client clicks the unsubscribe link.
        await using AppDbContext unsubDb = fixture.CreateDbContext(studioId);
        IMarketingOptOutSigner signer = Substitute.For<IMarketingOptOutSigner>();
        Guid capturedClientId = client.Id;
        signer.TryValidate("token", out Arg.Any<Guid>())
            .Returns(x => { x[1] = capturedClientId; return true; });
        WithdrawMarketingOptInHandler unsubHandler = new(unsubDb, signer);
        await unsubHandler.Handle(new WithdrawMarketingOptInCommand("token"), default);

        await using AppDbContext verify1 = fixture.CreateDbContext(studioId);
        (await verify1.Clients.FirstAsync(c => c.Id == client.Id)).MarketingOptIn.Should().BeFalse();

        // 2. A campaign sent afterward excludes this client.
        CampaignResponse draft = await RunCreateCampaign(studioId, new CreateCampaignRequest(
            "Follow-up", "<p>Hi again</p>", nameof(CampaignAudience.AllClients)));
        await RunSendCampaign(studioId, draft.Id, Substitute.For<IJobScheduler>());

        INotificationService notifications = Substitute.For<INotificationService>();
        IMarketingOptOutSigner jobSigner = Substitute.For<IMarketingOptOutSigner>();
        await using AppDbContext jobDb = fixture.CreateDbContext(studioId);
        SendCampaignJob job = new(jobDb, notifications, jobSigner, NullLogger<SendCampaignJob>.Instance);
        await job.RunAsync(draft.Id);

        await notifications.DidNotReceive().SendEmailAsync(
            client.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using AppDbContext verify2 = fixture.CreateDbContext(studioId);
        (await verify2.Campaigns.FirstAsync(c => c.Id == draft.Id)).DeliveredCount.Should().Be(0);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedStudio()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Campaign Test Studio",
            Slug = ("camp-" + Guid.NewGuid().ToString("N"))[..20],
            City = "Porto",
            OwnerEmail = $"camp{Guid.NewGuid():N}@test.com",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        db.Studios.Add(studio);
        await db.SaveChangesAsync();
        return studio.Id;
    }

    private async Task SeedActiveSubscription(Guid studioId, bool allowMarketingCampaigns)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Plan plan = new() { Name = "Pro Campaign Test", AllowMarketingCampaigns = allowMarketingCampaigns };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        db.Plans.Add(plan);
        db.Subscriptions.Add(new Subscription { StudioId = studioId, PlanId = plan.Id });
        await db.SaveChangesAsync();
    }

    private async Task<Client> SeedClient(Guid studioId, bool marketingOptIn)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Client client = new()
        {
            StudioId = studioId,
            FirstName = "C",
            LastName = "D",
            Email = $"{Guid.NewGuid():N}@client.test",
            MarketingOptIn = marketingOptIn,
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    private async Task<CampaignResponse> RunCreateCampaign(Guid studioId, CreateCampaignRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        CreateCampaignHandler handler = new(db, TenantFor(studioId));
        return await handler.Handle(new CreateCampaignCommand(req), default);
    }

    private async Task<CampaignResponse> RunSendCampaign(Guid studioId, Guid campaignId, IJobScheduler jobs)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        SendCampaignHandler handler = new(db, TenantFor(studioId), jobs);
        return await handler.Handle(new SendCampaignCommand(campaignId), default);
    }

    private static ICurrentTenant TenantFor(Guid studioId)
    {
        CurrentTenantService t = new();
        t.SetTenant(studioId);
        return t;
    }
}
