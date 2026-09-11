using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class ConnectPokAccountHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ISecretsProvider _secrets = Substitute.For<ISecretsProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    private ConnectPokAccountHandler CreateSut() => new(_db, _secrets);

    [Fact]
    public async Task Handle_NoExistingCredentialRef_CreatesOneAndSetsMerchantId()
    {
        await SeedStudioAsync();
        var request = new ConnectPokAccountRequest("key-1", "secret-1", "merchant-1");

        await CreateSut().Handle(new ConnectPokAccountCommand(_studioId, request), default);

        _db.Studios.Single(s => s.Id == _studioId).PokMerchantId.Should().Be("merchant-1");
        StudioCredentialRef credentialRef = _db.StudioCredentialRefs.Single(c => c.StudioId == _studioId);
        credentialRef.Provider.Should().Be(CredentialProvider.Pok);
        credentialRef.SecretPath.Should().Be($"studios/{_studioId}/pok");
    }

    [Fact]
    public async Task Handle_NoExistingCredentialRef_WritesKeyIdAndKeySecretToVault()
    {
        await SeedStudioAsync();
        var request = new ConnectPokAccountRequest("key-1", "secret-1", "merchant-1");

        await CreateSut().Handle(new ConnectPokAccountCommand(_studioId, request), default);

        await _secrets.Received(1).SetSecretAsync(
            $"studios/{_studioId}/pok",
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d.Count == 2 && d["keyId"] == "key-1" && d["keySecret"] == "secret-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingCredentialRef_DoesNotDuplicateRow()
    {
        await SeedStudioAsync();
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = $"studios/{_studioId}/pok",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var request = new ConnectPokAccountRequest("new-key", "new-secret", "merchant-2");
        await CreateSut().Handle(new ConnectPokAccountCommand(_studioId, request), default);

        _db.StudioCredentialRefs.Count(c => c.StudioId == _studioId).Should().Be(1);
        _db.Studios.Single(s => s.Id == _studioId).PokMerchantId.Should().Be("merchant-2");
    }

    [Fact]
    public async Task Handle_ReconnectingWithNewCredentials_OverwritesVaultSecret()
    {
        await SeedStudioAsync();
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = $"studios/{_studioId}/pok",
        });
        await _db.SaveChangesAsync();

        var request = new ConnectPokAccountRequest("rotated-key", "rotated-secret", "merchant-1");
        await CreateSut().Handle(new ConnectPokAccountCommand(_studioId, request), default);

        await _secrets.Received(1).SetSecretAsync(
            $"studios/{_studioId}/pok",
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["keyId"] == "rotated-key" && d["keySecret"] == "rotated-secret"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VaultWriteFails_ForFirstConnect_RevertsMerchantIdAndRemovesCredentialRef()
    {
        await SeedStudioAsync();
        _secrets.SetSecretAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("vault unreachable")));
        var request = new ConnectPokAccountRequest("key-1", "secret-1", "merchant-1");

        Func<Task> act = () => CreateSut().Handle(new ConnectPokAccountCommand(_studioId, request), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.Studios.Single(s => s.Id == _studioId).PokMerchantId.Should().BeNull();
        _db.StudioCredentialRefs.Any(c => c.StudioId == _studioId).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_VaultWriteFails_OnReconnect_RevertsMerchantIdButKeepsExistingCredentialRef()
    {
        await SeedStudioAsync();
        _db.Studios.Single(s => s.Id == _studioId).PokMerchantId = "old-merchant";
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = $"studios/{_studioId}/pok",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _secrets.SetSecretAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("vault unreachable")));
        var request = new ConnectPokAccountRequest("rotated-key", "rotated-secret", "new-merchant");

        Func<Task> act = () => CreateSut().Handle(new ConnectPokAccountCommand(_studioId, request), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        // The DB must end up back where it started (old-merchant), not left pointing at the new
        // merchant while Vault's write for it failed — that would be the mismatched-pair bug.
        _db.Studios.Single(s => s.Id == _studioId).PokMerchantId.Should().Be("old-merchant");
        _db.StudioCredentialRefs.Count(c => c.StudioId == _studioId).Should().Be(1);
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundAndNeverTouchesVault()
    {
        var request = new ConnectPokAccountRequest("key", "secret", "merchant");

        Func<Task> act = () => CreateSut().Handle(new ConnectPokAccountCommand(Guid.NewGuid(), request), default);

        await act.Should().ThrowAsync<NotFoundException>();
        await _secrets.DidNotReceive().SetSecretAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Command_IsAuditableForPokAccountConnected()
    {
        var request = new ConnectPokAccountRequest("key", "secret", "merchant");
        IAuditableCommand command = new ConnectPokAccountCommand(_studioId, request);

        command.AuditAction.Should().Be("Studio.PokAccountConnected");
        command.AuditTargetType.Should().Be("Studio");
        command.AuditTargetId.Should().Be(_studioId);
    }

    private async Task SeedStudioAsync()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
