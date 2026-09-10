using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.PromoCodes;

public class CreatePromoCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CreatePromoCodeHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreatePromoCodeHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_FixedAmountCode_ReturnsPromoCodeResponse()
    {
        CreatePromoCodeRequest req = new("SAVE20", 20m, null, true);

        PromoCodeResponse result = await CreateSut().Handle(new CreatePromoCodeCommand(req), default);

        result.Code.Should().Be("SAVE20");
        result.AmountFixed.Should().Be(20m);
        result.AmountPercent.Should().BeNull();
        result.IsActive.Should().BeTrue();
        result.StudioId.Should().Be(_studioId);
        result.RedemptionCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_LowercaseCode_NormalizesToUppercase()
    {
        CreatePromoCodeRequest req = new("save20", 20m, null, true);

        PromoCodeResponse result = await CreateSut().Handle(new CreatePromoCodeCommand(req), default);

        result.Code.Should().Be("SAVE20");
    }

    [Fact]
    public async Task Handle_PercentAmountCode_ReturnsPromoCodeResponse()
    {
        CreatePromoCodeRequest req = new("TENPCT", null, 10m, true);

        PromoCodeResponse result = await CreateSut().Handle(new CreatePromoCodeCommand(req), default);

        result.AmountFixed.Should().BeNull();
        result.AmountPercent.Should().Be(10m);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsCodeToDb()
    {
        CreatePromoCodeRequest req = new("SAVE20", 20m, null, true);

        await CreateSut().Handle(new CreatePromoCodeCommand(req), default);

        _db.PromoCodes.Should().ContainSingle(p => p.Code == "SAVE20" && p.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_WithExpiryAndMaxRedemptions_PersistsThem()
    {
        DateTime expires = DateTime.UtcNow.AddDays(30);
        CreatePromoCodeRequest req = new("LIMITED", 10m, null, true, expires, 5);

        PromoCodeResponse result = await CreateSut().Handle(new CreatePromoCodeCommand(req), default);

        result.ExpiresAt.Should().Be(expires);
        result.MaxRedemptions.Should().Be(5);
    }
}
