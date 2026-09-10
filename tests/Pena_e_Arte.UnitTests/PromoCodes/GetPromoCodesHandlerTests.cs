using FluentAssertions;
using Pena_e_Arte.Application.PromoCodes.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.PromoCodes;

public class GetPromoCodesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetPromoCodesHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ActiveAndInactiveCodes_ReturnsActiveFirst()
    {
        _db.PromoCodes.Add(new PromoCode { StudioId = _studioId, Code = "INACTIVE", AmountFixed = 5m, IsActive = false });
        _db.PromoCodes.Add(new PromoCode { StudioId = _studioId, Code = "ACTIVE", AmountFixed = 5m, IsActive = true });
        await _db.SaveChangesAsync();

        List<PromoCodeResponse> result = await CreateSut().Handle(new GetPromoCodesQuery(), default);

        result.Should().HaveCount(2);
        result[0].IsActive.Should().BeTrue();
    }
}
