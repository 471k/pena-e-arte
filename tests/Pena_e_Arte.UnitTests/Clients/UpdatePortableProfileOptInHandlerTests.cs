using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdatePortableProfileOptInHandlerTests
{
    private readonly FakeDbContext _db          = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant     = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser   _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid           _studioId   = Guid.NewGuid();
    private readonly Guid           _userId     = Guid.NewGuid();

    public UpdatePortableProfileOptInHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.UserId.Returns(_userId);
    }

    private UpdatePortableProfileOptInHandler CreateSut() =>
        new(_db, _currentUser);

    private async Task<(Client client, ClientProfile profile)> SeedClientWithProfileAsync()
    {
        Client client = new()
        {
            StudioId  = _studioId,
            UserId    = _userId,
            FirstName = "Ana",
            LastName  = "Costa",
            Email     = "ana@example.com"
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        ClientProfile profile = new()
        {
            StudioId = _studioId,
            ClientId = client.Id,
        };
        _db.ClientProfiles.Add(profile);
        await _db.SaveChangesAsync();

        return (client, profile);
    }

    [Fact]
    public async Task Handle_OptIn_SetsAllowCrossTenantReadTrue()
    {
        (_, ClientProfile profile) = await SeedClientWithProfileAsync();

        await CreateSut().Handle(
            new UpdatePortableProfileOptInCommand(new UpdatePortableProfileOptInRequest(OptIn: true)),
            default);

        profile.AllowCrossTenantRead.Should().BeTrue();
        profile.CrossTenantOptInAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_OptOut_ClearsAllowCrossTenantRead()
    {
        (_, ClientProfile profile) = await SeedClientWithProfileAsync();
        profile.OptInToCrossTenant();
        await _db.SaveChangesAsync();

        await CreateSut().Handle(
            new UpdatePortableProfileOptInCommand(new UpdatePortableProfileOptInRequest(OptIn: false)),
            default);

        profile.AllowCrossTenantRead.Should().BeFalse();
        profile.CrossTenantOptInAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ClientNotFound_ThrowsNotFoundException()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePortableProfileOptInCommand(new UpdatePortableProfileOptInRequest(OptIn: true)),
            default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ProfileNotFound_ThrowsNotFoundException()
    {
        Client client = new()
        {
            StudioId  = _studioId,
            UserId    = _userId,
            FirstName = "Rui",
            LastName  = "Neves",
            Email     = "rui@example.com"
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePortableProfileOptInCommand(new UpdatePortableProfileOptInRequest(OptIn: true)),
            default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
