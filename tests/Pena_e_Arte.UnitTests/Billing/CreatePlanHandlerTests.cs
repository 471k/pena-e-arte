using FluentAssertions;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CreatePlanHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private CreatePlanHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsPlanResponse()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Pro", 17, [new PlanPriceRequest("Monthly", 49m)])), default);

        result.Name.Should().Be("Pro");
        result.Prices.Should().ContainSingle(p => p.Interval == "Monthly" && p.Price == 49m);
        result.SubscriberCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsPlan()
    {
        await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Basic", 17, [new PlanPriceRequest("Yearly", 290m)])), default);

        _db.Plans.Should().ContainSingle(p => p.Name == "Basic");
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsNewId()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Enterprise", 17, [new PlanPriceRequest("Monthly", 99m)])), default);

        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithStripePriceIds_PersistsAndReturnsThem()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Pro", 17,
                [
                    new PlanPriceRequest("Monthly", 49m, StripePriceId: "price_monthly_xyz"),
                    new PlanPriceRequest("Yearly", 490m, StripePriceId: "price_yearly_xyz"),
                ])), default);

        result.Prices.Single(p => p.Interval == "Monthly").StripePriceId.Should().Be("price_monthly_xyz");
        result.Prices.Single(p => p.Interval == "Yearly").StripePriceId.Should().Be("price_yearly_xyz");
        _db.PlanPrices.Single(p => p.Interval == Domain.Enums.BillingInterval.Monthly)
            .StripePriceId.Should().Be("price_monthly_xyz");
    }

    [Fact]
    public async Task Handle_InvalidBillingInterval_ThrowsException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Pro", 17, [new PlanPriceRequest("Weekly", 49m)])), default);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Handle_WithLimitFields_PersistsAndReturnsThem()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Premium", 17, [new PlanPriceRequest("Monthly", 79m)],
                MaxArtists: 6,
                MaxAppointmentsPerMonth: 400,
                MaxNotificationsPerMonth: 1200,
                MaxStorageGb: 25,
                MaxLocations: 2,
                AllowApiAccess: false,
                PrioritySupport: true)), default);

        result.MaxArtists.Should().Be(6);
        result.MaxAppointmentsPerMonth.Should().Be(400);
        result.MaxNotificationsPerMonth.Should().Be(1200);
        result.MaxStorageGb.Should().Be(25);
        result.MaxLocations.Should().Be(2);
        result.PrioritySupport.Should().BeTrue();
        result.AllowApiAccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoLimitFields_ReturnsNullMeaningUnlimited()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Pro", 17, [new PlanPriceRequest("Monthly", 99m)])), default);

        result.MaxArtists.Should().BeNull();
        result.MaxAppointmentsPerMonth.Should().BeNull();
        result.MaxStorageGb.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MultiplePrices_PersistsBothIntervals()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Premium", 17,
                [
                    new PlanPriceRequest("Monthly", 79m),
                    new PlanPriceRequest("Yearly", 790m),
                ])), default);

        result.Prices.Should().HaveCount(2);
        _db.PlanPrices.Count(p => p.PlanId == result.Id).Should().Be(2);
    }
}
