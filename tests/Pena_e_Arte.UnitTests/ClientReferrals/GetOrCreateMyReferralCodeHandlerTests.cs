using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.ClientReferrals.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ClientReferrals;

public class GetOrCreateMyReferralCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetOrCreateMyReferralCodeHandler CreateSut() => new(_db, _user);

    private Client SeedClient()
    {
        Guid userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        Studio studio = new() { Id = _studioId, Name = "Test", Slug = "test-studio", City = "Porto", OwnerEmail = "x@x.com", IsActive = true, TrialExpiresAt = DateTime.UtcNow.AddDays(14) };
        Client client = new() { StudioId = _studioId, UserId = userId, FirstName = "A", LastName = "B", Email = "a@b.com" };
        _db.Studios.Add(studio);
        _db.Clients.Add(client);
        _db.SaveChanges();
        return client;
    }

    [Fact]
    public async Task Handle_FirstCall_CreatesNewCode()
    {
        SeedClient();

        ClientReferralCodeResponse result = await CreateSut().Handle(new GetOrCreateMyReferralCodeCommand(), default);

        result.Code.Should().HaveLength(8);
        result.RewardPercent.Should().BeGreaterThan(0m);
        result.RedemptionCount.Should().Be(0);
        result.ShareUrl.Should().Contain(result.Code);
        result.ShareUrl.Should().Contain("test-studio");
        _db.ClientReferralCodes.Should().ContainSingle(c => c.Code == result.Code);
    }

    [Fact]
    public async Task Handle_SecondCall_ReturnsSameExistingCode()
    {
        SeedClient();

        ClientReferralCodeResponse first = await CreateSut().Handle(new GetOrCreateMyReferralCodeCommand(), default);
        ClientReferralCodeResponse second = await CreateSut().Handle(new GetOrCreateMyReferralCodeCommand(), default);

        second.Id.Should().Be(first.Id);
        second.Code.Should().Be(first.Code);
        _db.ClientReferralCodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_NoClientRecordForUser_ThrowsNotFoundException()
    {
        _user.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new GetOrCreateMyReferralCodeCommand(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
