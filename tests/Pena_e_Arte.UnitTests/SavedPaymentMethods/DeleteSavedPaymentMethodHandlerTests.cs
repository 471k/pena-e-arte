using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.SavedPaymentMethods.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.SavedPaymentMethods;

public class DeleteSavedPaymentMethodHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    private DeleteSavedPaymentMethodHandler CreateSut() => new(_db, _user);

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
    public async Task Handle_OwnMethod_SetsDeletedAt()
    {
        Client client = SeedClient();
        SavedPaymentMethod method = new() { StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-1" };
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeleteSavedPaymentMethodCommand(method.Id), default);

        _db.SavedPaymentMethods.Single(s => s.Id == method.Id).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DeletingTheDefault_PromotesNextMostRecentToDefault()
    {
        Client client = SeedClient();
        SavedPaymentMethod older = new()
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-old",
            IsDefault = false, CreatedAt = DateTime.UtcNow.AddDays(-2),
        };
        SavedPaymentMethod newest = new()
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-new",
            IsDefault = false, CreatedAt = DateTime.UtcNow.AddDays(-1),
        };
        SavedPaymentMethod defaultMethod = new()
        {
            StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-default", IsDefault = true,
        };
        _db.SavedPaymentMethods.AddRange(older, newest, defaultMethod);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeleteSavedPaymentMethodCommand(defaultMethod.Id), default);

        _db.SavedPaymentMethods.Single(s => s.Id == newest.Id).IsDefault.Should().BeTrue();
        _db.SavedPaymentMethods.Single(s => s.Id == older.Id).IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DeletingOnlyMethod_LeavesNoDefaultBehind()
    {
        Client client = SeedClient();
        SavedPaymentMethod method = new() { StudioId = _studioId, ClientId = client.Id, ProviderCardTokenId = "card-1", IsDefault = true };
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeleteSavedPaymentMethodCommand(method.Id), default);

        _db.SavedPaymentMethods.Single(s => s.Id == method.Id).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_MethodBelongingToAnotherClient_ThrowsNotFoundException()
    {
        SeedClient();
        SavedPaymentMethod othersMethod = new()
        {
            StudioId = _studioId, ClientId = Guid.NewGuid(), ProviderCardTokenId = "not-yours",
        };
        _db.SavedPaymentMethods.Add(othersMethod);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new DeleteSavedPaymentMethodCommand(othersMethod.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        SeedClient();

        Func<Task> act = () => CreateSut().Handle(new DeleteSavedPaymentMethodCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
