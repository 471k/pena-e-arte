using FluentAssertions;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
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
            new CreatePlanCommand(new CreatePlanRequest("Pro", "Monthly", 49m, 490m, 17)), default);

        result.Name.Should().Be("Pro");
        result.BillingInterval.Should().Be("Monthly");
        result.PriceMonthly.Should().Be(49m);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsPlan()
    {
        await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest("Basic", "Yearly", 29m, 290m, 17)), default);

        _db.Plans.Should().ContainSingle(p => p.Name == "Basic");
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsNewId()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest("Enterprise", "Monthly", 99m, 990m, 17)), default);

        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithStripePriceIds_PersistsAndReturnsThem()
    {
        PlanResponse result = await CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest(
                "Pro", "Monthly", 49m, 490m, 17,
                StripePriceIdMonthly: "price_monthly_xyz",
                StripePriceIdYearly:  "price_yearly_xyz")), default);

        result.StripePriceIdMonthly.Should().Be("price_monthly_xyz");
        result.StripePriceIdYearly.Should().Be("price_yearly_xyz");
        _db.Plans.Single(p => p.Name == "Pro").StripePriceIdMonthly.Should().Be("price_monthly_xyz");
    }

    [Fact]
    public async Task Handle_InvalidBillingInterval_ThrowsException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new CreatePlanCommand(new CreatePlanRequest("Pro", "Weekly", 49m, 490m, 17)), default);

        await act.Should().ThrowAsync<Exception>();
    }
}
