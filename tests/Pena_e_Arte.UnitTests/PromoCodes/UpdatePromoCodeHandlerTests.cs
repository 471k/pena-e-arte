using FluentAssertions;
using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.PromoCodes;

public class UpdatePromoCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private UpdatePromoCodeHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingCode_UpdatesFields()
    {
        PromoCode existing = new() { StudioId = _studioId, Code = "OLD", AmountFixed = 10m, IsActive = false };
        _db.PromoCodes.Add(existing);
        await _db.SaveChangesAsync();

        UpdatePromoCodeRequest req = new("NEW", 25m, null, true);
        PromoCodeResponse result = await CreateSut().Handle(new UpdatePromoCodeCommand(existing.Id, req), default);

        result.Code.Should().Be("NEW");
        result.AmountFixed.Should().Be(25m);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownId_ThrowsNotFoundException()
    {
        UpdatePromoCodeRequest req = new("NEW", 25m, null, true);

        Func<Task> act = () => CreateSut().Handle(new UpdatePromoCodeCommand(Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
