using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class WithdrawMarketingOptInHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IMarketingOptOutSigner _signer = Substitute.For<IMarketingOptOutSigner>();

    private WithdrawMarketingOptInHandler CreateSut() => new(_db, _signer);

    [Fact]
    public async Task Handle_ValidToken_FlipsMarketingOptInToFalse()
    {
        Client client = new() { StudioId = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", MarketingOptIn = true };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Guid captured = client.Id;
        _signer.TryValidate("valid-token", out Arg.Any<Guid>())
            .Returns(x => { x[1] = captured; return true; });

        await CreateSut().Handle(new WithdrawMarketingOptInCommand("valid-token"), default);

        _db.Clients.Single(c => c.Id == client.Id).MarketingOptIn.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_InvalidToken_ThrowsBusinessRuleViolationException()
    {
        _signer.TryValidate("bad-token", out Arg.Any<Guid>()).Returns(false);

        Func<Task> act = () => CreateSut().Handle(new WithdrawMarketingOptInCommand("bad-token"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_UnknownClientId_DoesNotThrow()
    {
        _signer.TryValidate("valid-token", out Arg.Any<Guid>())
            .Returns(x => { x[1] = Guid.NewGuid(); return true; });

        Func<Task> act = () => CreateSut().Handle(new WithdrawMarketingOptInCommand("valid-token"), default);

        await act.Should().NotThrowAsync();
    }
}
