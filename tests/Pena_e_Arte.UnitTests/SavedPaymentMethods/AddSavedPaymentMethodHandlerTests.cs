using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.SavedPaymentMethods.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.SavedPaymentMethods;

public class AddSavedPaymentMethodHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IPokCardTokenService _cardTokens = Substitute.For<IPokCardTokenService>();
    private readonly Guid _studioId = Guid.NewGuid();

    public AddSavedPaymentMethodHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private AddSavedPaymentMethodHandler CreateSut() => new(_db, _tenant, _user, _cardTokens);

    private static readonly AddSavedPaymentMethodRequest Request = new(
        "jwe-value", "123", "Jamie", "Client", "jamie@test.com", "AL", "Tirana", "Tirana",
        "Rr. Myslym Shyri 10", "1001", "+355691234567");

    private Client SeedClient()
    {
        Guid userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        Client client = new() { StudioId = _studioId, UserId = userId, FirstName = "A", LastName = "B", Email = "a@b.com" };
        _db.Clients.Add(client);
        _db.SaveChanges();
        return client;
    }

    [Fact]
    public async Task Handle_ValidRequest_TokenizesAndPersistsSavedMethod()
    {
        SeedClient();
        _cardTokens.TokenizeCardAsync(_studioId, "jwe-value", Arg.Any<string?>(), Arg.Any<PokCardBillingInfo>(), Arg.Any<CancellationToken>())
            .Returns(new PokTokenizedCard("card-abc", "Visa", "**** 4242", "12", "2030"));

        SavedPaymentMethodResponse result = await CreateSut().Handle(new AddSavedPaymentMethodCommand(Request), default);

        result.CardBrand.Should().Be("Visa");
        result.MaskedPan.Should().Be("**** 4242");
        result.IsDefault.Should().BeTrue();
        _db.SavedPaymentMethods.Should().ContainSingle(s => s.ProviderCardTokenId == "card-abc" && s.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_FirstSavedMethod_IsDefault()
    {
        SeedClient();
        _cardTokens.TokenizeCardAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PokCardBillingInfo>(), Arg.Any<CancellationToken>())
            .Returns(new PokTokenizedCard("card-1", null, null, null, null));

        SavedPaymentMethodResponse result = await CreateSut().Handle(new AddSavedPaymentMethodCommand(Request), default);

        result.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SecondSavedMethod_IsNotDefault()
    {
        Client client = SeedClient();
        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-existing", IsDefault = true,
        });
        await _db.SaveChangesAsync();

        _cardTokens.TokenizeCardAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PokCardBillingInfo>(), Arg.Any<CancellationToken>())
            .Returns(new PokTokenizedCard("card-2", null, null, null, null));

        SavedPaymentMethodResponse result = await CreateSut().Handle(new AddSavedPaymentMethodCommand(Request), default);

        result.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ThinTokenizeResponse_DegradesToNullDisplayFieldsWithoutFailing()
    {
        SeedClient();
        _cardTokens.TokenizeCardAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PokCardBillingInfo>(), Arg.Any<CancellationToken>())
            .Returns(new PokTokenizedCard("card-thin", null, null, null, null));

        SavedPaymentMethodResponse result = await CreateSut().Handle(new AddSavedPaymentMethodCommand(Request), default);

        result.CardBrand.Should().BeNull();
        result.MaskedPan.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RetriedTokenForSameCard_UpdatesExistingRowInsteadOfDuplicating()
    {
        Client client = SeedClient();
        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-dup",
            CardBrand = "Mastercard", IsDefault = true,
        });
        await _db.SaveChangesAsync();

        _cardTokens.TokenizeCardAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PokCardBillingInfo>(), Arg.Any<CancellationToken>())
            .Returns(new PokTokenizedCard("card-dup", "Visa", "**** 1111", "1", "2031"));

        SavedPaymentMethodResponse result = await CreateSut().Handle(new AddSavedPaymentMethodCommand(Request), default);

        result.CardBrand.Should().Be("Visa");
        _db.SavedPaymentMethods.Should().ContainSingle(s => s.ProviderCardTokenId == "card-dup");
    }

    [Fact]
    public async Task Handle_NoClientRecordForUser_ThrowsNotFoundException()
    {
        _user.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new AddSavedPaymentMethodCommand(Request), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
