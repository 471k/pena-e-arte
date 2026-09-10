using FluentAssertions;
using Pena_e_Arte.Application.Campaigns;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Campaigns;

public class CampaignAudienceExtensionsTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private Client SeedClient(bool marketingOptIn, string email = null!)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "A",
            LastName = "B",
            Email = email ?? $"{Guid.NewGuid()}@test.com",
            MarketingOptIn = marketingOptIn,
        };
        _db.Clients.Add(client);
        _db.SaveChanges();
        return client;
    }

    [Fact]
    public async Task ResolveCampaignAudienceAsync_AllClients_ExcludesOptedOutClients()
    {
        Client optedIn = SeedClient(marketingOptIn: true);
        SeedClient(marketingOptIn: false);

        Campaign campaign = new() { StudioId = _studioId, Audience = CampaignAudience.AllClients };

        List<Client> audience = await _db.ResolveCampaignAudienceAsync(campaign, default);

        audience.Should().ContainSingle(c => c.Id == optedIn.Id);
    }

    [Fact]
    public async Task ResolveCampaignAudienceAsync_ClientsWithNoRecentVisit_ExcludesRecentlyCompletedAndOptedOut()
    {
        Client recentVisitor = SeedClient(marketingOptIn: true);
        Client staleVisitor = SeedClient(marketingOptIn: true);
        Client optedOutStale = SeedClient(marketingOptIn: false);

        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ClientId = recentVisitor.Id,
            Date = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(-10).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Completed,
            DepositStatus = DepositStatus.Paid,
        });
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ClientId = staleVisitor.Id,
            Date = DateTime.UtcNow.AddDays(-200),
            EndDate = DateTime.UtcNow.AddDays(-200).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Completed,
            DepositStatus = DepositStatus.Paid,
        });
        await _db.SaveChangesAsync();
        _ = optedOutStale;

        Campaign campaign = new()
        {
            StudioId = _studioId,
            Audience = CampaignAudience.ClientsWithNoRecentVisit,
            NoRecentVisitDays = 90,
        };

        List<Client> audience = await _db.ResolveCampaignAudienceAsync(campaign, default);

        audience.Should().ContainSingle(c => c.Id == staleVisitor.Id);
    }

    [Fact]
    public async Task ResolveCampaignAudienceAsync_Custom_StillHardFiltersOnMarketingOptIn()
    {
        Client included = SeedClient(marketingOptIn: true);
        Client optedOutButListed = SeedClient(marketingOptIn: false);

        Campaign campaign = new()
        {
            StudioId = _studioId,
            Audience = CampaignAudience.Custom,
            CustomClientIds = [included.Id, optedOutButListed.Id],
        };

        List<Client> audience = await _db.ResolveCampaignAudienceAsync(campaign, default);

        audience.Should().ContainSingle(c => c.Id == included.Id);
    }

    [Fact]
    public async Task ResolveCampaignAudienceAsync_Custom_ExcludesClientsNotInList()
    {
        Client included = SeedClient(marketingOptIn: true);
        SeedClient(marketingOptIn: true); // opted in but not on the custom list

        Campaign campaign = new()
        {
            StudioId = _studioId,
            Audience = CampaignAudience.Custom,
            CustomClientIds = [included.Id],
        };

        List<Client> audience = await _db.ResolveCampaignAudienceAsync(campaign, default);

        audience.Should().ContainSingle(c => c.Id == included.Id);
    }
}
