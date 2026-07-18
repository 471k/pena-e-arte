using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Billing;

public class GetPlanUsageHandlerTests
{
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    private GetPlanUsageHandler CreateSut() => new(_planLimits);

    [Fact]
    public async Task Handle_NoSubscription_ReturnsNull()
    {
        _planLimits.GetUsageSnapshotAsync(Arg.Any<CancellationToken>()).Returns((PlanUsageSnapshot?)null);

        PlanUsageResponse? result = await CreateSut().Handle(new GetPlanUsageQuery(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AllUnlimited_ReturnsNullMaxForEveryDimension()
    {
        PlanUsageSnapshot snapshot = new(
            "Pro",
            new PlanUsageDimension(4, null),
            new PlanUsageDimension(12, null),
            new PlanUsageDimension(50, null),
            new PlanUsageDimension(3.2, null),
            new PlanUsageDimension(1, null));
        _planLimits.GetUsageSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);

        PlanUsageResponse? result = await CreateSut().Handle(new GetPlanUsageQuery(), default);

        result.Should().NotBeNull();
        result!.Artists.Max.Should().BeNull();
        result.AppointmentsPerMonth.Max.Should().BeNull();
        result.NotificationsPerMonth.Max.Should().BeNull();
        result.StorageGb.Max.Should().BeNull();
        result.Locations.Max.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MixedLimits_ReturnsCorrectCurrentAndMaxPerDimension()
    {
        PlanUsageSnapshot snapshot = new(
            "Starter",
            new PlanUsageDimension(1, 1),
            new PlanUsageDimension(38, 40),
            new PlanUsageDimension(120, 150),
            new PlanUsageDimension(1.8, 2),
            new PlanUsageDimension(1, 1));
        _planLimits.GetUsageSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);

        PlanUsageResponse? result = await CreateSut().Handle(new GetPlanUsageQuery(), default);

        result.Should().NotBeNull();
        result!.PlanName.Should().Be("Starter");
        result.Artists.Should().Be(new PlanUsageDimensionResponse(1, 1));
        result.AppointmentsPerMonth.Should().Be(new PlanUsageDimensionResponse(38, 40));
        result.NotificationsPerMonth.Should().Be(new PlanUsageDimensionResponse(120, 150));
        result.StorageGb.Should().Be(new PlanUsageDimensionResponse(1.8, 2));
        result.Locations.Should().Be(new PlanUsageDimensionResponse(1, 1));
    }
}
