using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

/// <summary>An identity service that knows no accounts, for tests that are not about actor names.</summary>
internal static class AuditLogTestIdentity
{
    public static IIdentityService NoAccounts()
    {
        IIdentityService identity = Substitute.For<IIdentityService>();
        identity.GetAccountGivenNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?>());
        return identity;
    }
}

/// <summary>
/// Phi's decision, 19 Nov: the admin audit log shows a staff actor's CURRENT name, looked up when the page is read and
/// never stored with the entry. Clients are never named. A deleted account says so.
/// </summary>
public class GetAuditLogActorNameTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _artistNoName = Guid.NewGuid();
    private readonly Guid _deletedAdmin = Guid.NewGuid();
    private readonly Guid _client = Guid.NewGuid();

    private IReadOnlyCollection<Guid>? _askedFor;

    public GetAuditLogActorNameTests()
    {
        _identity.GetAccountGivenNamesAsync(Arg.Do<IReadOnlyCollection<Guid>>(ids => _askedFor = ids), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?>
            {
                [_owner] = "Maria",
                [_artistNoName] = null,
                [_client] = "Should never be shown", // even if Identity knows it, a client is not named
            });
    }

    [Fact]
    public async Task StaffAreNamed_ClientsAreNot_DeletedAccountsSaySo()
    {
        await Seed(_owner, "owner");
        await Seed(_owner, "owner");
        await Seed(_artistNoName, "artist");
        await Seed(_deletedAdmin, "admin");
        await Seed(_client, "client");

        AuditLogPageResponse page = await new GetAuditLogHandler(_db, _identity).Handle(new GetAuditLogQuery(), default);

        page.Items.Where(i => i.ActorUserId == _owner).Should().HaveCount(2)
            .And.OnlyContain(i => i.ActorName == "Maria" && !i.ActorAccountDeleted);
        AuditLogEntryResponse artist = page.Items.Single(i => i.ActorUserId == _artistNoName);
        artist.ActorName.Should().BeNull();
        artist.ActorAccountDeleted.Should().BeFalse();
        AuditLogEntryResponse admin = page.Items.Single(i => i.ActorUserId == _deletedAdmin);
        admin.ActorName.Should().BeNull();
        admin.ActorAccountDeleted.Should().BeTrue();
        AuditLogEntryResponse client = page.Items.Single(i => i.ActorUserId == _client);
        client.ActorName.Should().BeNull();
        client.ActorAccountDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task OnlyThePagesDistinctStaffIdsAreLookedUp_NeverAClient()
    {
        await Seed(_owner, "owner");
        await Seed(_owner, "owner");
        await Seed(_deletedAdmin, "admin-impersonating");
        await Seed(_client, "client");

        await new GetAuditLogHandler(_db, _identity).Handle(new GetAuditLogQuery(), default);

        _askedFor.Should().BeEquivalentTo([_owner, _deletedAdmin]);
    }

    [Fact]
    public async Task AnImpersonatingAdminIsNamedAsTheRealAdmin()
    {
        await Seed(_owner, "admin-impersonating");

        AuditLogPageResponse page = await new GetAuditLogHandler(_db, _identity).Handle(new GetAuditLogQuery(), default);

        page.Items.Single().ActorName.Should().Be("Maria");
        page.Items.Single().ActorRole.Should().Be("admin-impersonating");
    }

    private async Task Seed(Guid actorUserId, string role)
    {
        _db.AuditLogEntries.Add(AuditLogEntry.Create(actorUserId, role, "Studio.Suspended", "Studio", Guid.NewGuid(), Guid.NewGuid(), "{}"));
        await _db.SaveChangesAsync();
    }
}
