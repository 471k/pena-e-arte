using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Messaging.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

// Exercises ConversationEligibility (internal, not directly testable — no
// InternalsVisibleTo in this codebase, same reasoning FeedbackAccessGuard is only
// tested indirectly via its handlers) through its public entry point.
public class GetConversationContactsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetConversationContactsHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _tenant.IsSet.Returns(true);
    }

    private GetConversationContactsHandler CreateSut() => new(_db, _user, _tenant, _identity);

    [Fact]
    public async Task Handle_Client_CanReachAssignedArtist()
    {
        Guid clientUserId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Artist artist = SeedArtist(artistUserId, isActive: true);
        SeedClient(clientUserId, assignedArtistId: artist.Id);
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");
        NoOwner();

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().ContainSingle(c => c.UserId == artistUserId && c.Role == "artist");
    }

    [Fact]
    public async Task Handle_Client_CanReachArtistFromAppointmentOnly_NotJustAssigned()
    {
        Guid clientUserId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Artist artist = SeedArtist(artistUserId, isActive: true);
        Client client = SeedClient(clientUserId, assignedArtistId: null);
        SeedAppointment(client.Id, artist.Id);
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");
        NoOwner();

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().ContainSingle(c => c.UserId == artistUserId && c.Role == "artist");
    }

    [Fact]
    public async Task Handle_Client_CannotReachUnrelatedArtist()
    {
        Guid clientUserId = Guid.NewGuid();
        SeedArtist(Guid.NewGuid(), isActive: true); // unrelated artist, no appointment/assignment
        SeedClient(clientUserId, assignedArtistId: null);
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");
        NoOwner();

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Client_CanAlwaysReachResolvedOwner()
    {
        Guid clientUserId = Guid.NewGuid();
        Guid ownerUserId = Guid.NewGuid();
        SeedClient(clientUserId, assignedArtistId: null);
        SeedStudio("owner@studio.test");
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");
        _identity.GetUserIdByEmailAsync("owner@studio.test", Arg.Any<CancellationToken>()).Returns(ownerUserId);
        _identity.GetUserDisplayNameAsync("owner@studio.test", Arg.Any<CancellationToken>()).Returns("Studio Owner Name");

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().ContainSingle(c => c.UserId == ownerUserId && c.Role == "owner");
    }

    [Fact]
    public async Task Handle_Owner_CanReachEveryActiveArtistAndClient()
    {
        Guid ownerUserId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Guid clientUserId = Guid.NewGuid();
        SeedArtist(artistUserId, isActive: true);
        SeedArtist(Guid.NewGuid(), isActive: false); // inactive — excluded
        SeedClient(clientUserId, assignedArtistId: null);
        _user.UserId.Returns(ownerUserId);
        _user.Role.Returns("owner");

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().HaveCount(2);
        result.Should().Contain(c => c.UserId == artistUserId && c.Role == "artist");
        result.Should().Contain(c => c.UserId == clientUserId && c.Role == "client");
    }

    [Fact]
    public async Task Handle_ClientWithNoLinkedLogin_NeverAppearsInResults()
    {
        Guid ownerUserId = Guid.NewGuid();
        SeedClient(userId: null, assignedArtistId: null); // no linked login
        _user.UserId.Returns(ownerUserId);
        _user.Role.Returns("owner");

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Client_CannotReachDeactivatedArtist_EvenIfAssigned()
    {
        Guid clientUserId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Artist deactivatedArtist = SeedArtist(artistUserId, isActive: false);
        SeedClient(clientUserId, assignedArtistId: deactivatedArtist.Id);
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");
        NoOwner();

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Admin_NeverGetsAnyContacts()
    {
        SeedStudio("owner@studio.test");
        _user.UserId.Returns(Guid.NewGuid());
        _user.Role.Returns("admin");
        _identity.GetUserIdByEmailAsync("owner@studio.test", Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TenantNotSet_ReturnsEmptyList()
    {
        _tenant.IsSet.Returns(false);
        _user.UserId.Returns(Guid.NewGuid());
        _user.Role.Returns("client");

        List<ConversationContactResponse> result = await CreateSut().Handle(new GetConversationContactsQuery(), default);

        result.Should().BeEmpty();
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private Artist SeedArtist(Guid? userId, bool isActive)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Art",
            LastName = "Ist",
            Email = $"{Guid.NewGuid():N}@artist.test",
            IsActive = isActive,
        };
        _db.Artists.Add(artist);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return artist;
    }

    private Client SeedClient(Guid? userId, Guid? assignedArtistId)
    {
        Client client = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Cli",
            LastName = "Ent",
            Email = $"{Guid.NewGuid():N}@client.test",
            ArtistId = assignedArtistId,
        };
        _db.Clients.Add(client);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return client;
    }

    private void SeedAppointment(Guid clientId, Guid artistId)
    {
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ClientId = clientId,
            ArtistId = artistId,
            Date = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(1).AddHours(1),
            DurationMinutes = 60,
        });
        _db.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private void SeedStudio(string ownerEmail)
    {
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "Test Studio",
            Slug = _studioId.ToString("N")[..8],
            OwnerEmail = ownerEmail,
        });
        _db.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private void NoOwner()
    {
        // No Studio row seeded for _studioId — TryResolveOwnerAsync returns null,
        // so the owner contact is simply omitted (never throws).
    }
}
