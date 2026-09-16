using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.SavedPaymentMethods.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.SavedPaymentMethods;

public class GetSavedPaymentMethodsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetSavedPaymentMethodsHandler CreateSut() => new(_db, _user);

    private Client SeedClient(Guid? studioId = null)
    {
        Guid userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        Client client = new() { StudioId = studioId ?? _studioId, UserId = userId, FirstName = "A", LastName = "B", Email = "a@b.com" };
        _db.Clients.Add(client);
        _db.SaveChanges();
        return client;
    }

    [Fact]
    public async Task Handle_NoSavedMethods_ReturnsEmptyList()
    {
        SeedClient();

        List<SavedPaymentMethodResponse> result = await CreateSut().Handle(new GetSavedPaymentMethodsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DefaultOrdersFirstThenNewest()
    {
        Client client = SeedClient();
        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-old",
            IsDefault = false, CreatedAt = DateTime.UtcNow.AddDays(-2),
        });
        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-default",
            IsDefault = true, CreatedAt = DateTime.UtcNow.AddDays(-5),
        });
        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-newest",
            IsDefault = false, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        List<SavedPaymentMethodResponse> result = await CreateSut().Handle(new GetSavedPaymentMethodsQuery(), default);

        result.Should().HaveCount(3);
        result[0].IsDefault.Should().BeTrue();
        result[1].CreatedAt.Should().BeAfter(result[2].CreatedAt);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyCallersOwnMethods_NotAnotherClientsAtSameStudio()
    {
        Client client = SeedClient();
        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "mine",
        });
        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            StudioId = _studioId, ClientId = Guid.NewGuid(), ProviderCardTokenId = "not-mine",
        });
        await _db.SaveChangesAsync();

        List<SavedPaymentMethodResponse> result = await CreateSut().Handle(new GetSavedPaymentMethodsQuery(), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_NoClientRecordForUser_ThrowsNotFoundException()
    {
        _user.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new GetSavedPaymentMethodsQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
