using FluentAssertions;
using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.PromoCodes;

public class DeletePromoCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private DeletePromoCodeHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingCode_SoftDeletes()
    {
        PromoCode existing = new() { StudioId = _studioId, Code = "GONE", AmountFixed = 10m, IsActive = true };
        _db.PromoCodes.Add(existing);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeletePromoCodeCommand(existing.Id), default);

        _db.PromoCodes.Single(p => p.Id == existing.Id).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UnknownId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeletePromoCodeCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
