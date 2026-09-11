using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPokConnectionStatusHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetPokConnectionStatusHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private GetPokConnectionStatusHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NoStudioRow_ReportsNotConnected()
    {
        PokConnectionStatusResponse result = await CreateSut().Handle(new GetPokConnectionStatusQuery(), default);

        result.Connected.Should().BeFalse();
        result.MerchantId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MerchantIdSetButNoCredentialRef_ReportsNotConnected()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = "merchant-1" });
        await _db.SaveChangesAsync();

        PokConnectionStatusResponse result = await CreateSut().Handle(new GetPokConnectionStatusQuery(), default);

        result.Connected.Should().BeFalse();
        result.MerchantId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CredentialRefButNoMerchantId_ReportsNotConnected()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = null });
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = "studios/x/pok",
        });
        await _db.SaveChangesAsync();

        PokConnectionStatusResponse result = await CreateSut().Handle(new GetPokConnectionStatusQuery(), default);

        result.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_BothPresent_ReportsConnectedWithMerchantId()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = "merchant-1" });
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = "studios/x/pok",
        });
        await _db.SaveChangesAsync();

        PokConnectionStatusResponse result = await CreateSut().Handle(new GetPokConnectionStatusQuery(), default);

        result.Connected.Should().BeTrue();
        result.MerchantId.Should().Be("merchant-1");
    }

    [Fact]
    public async Task Handle_CredentialRefForDifferentStudio_IsNotCountedAsThisStudiosConnection()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = "merchant-1" });
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = Guid.NewGuid(),
            Provider = CredentialProvider.Pok,
            SecretPath = "studios/other/pok",
        });
        await _db.SaveChangesAsync();

        PokConnectionStatusResponse result = await CreateSut().Handle(new GetPokConnectionStatusQuery(), default);

        result.Connected.Should().BeFalse();
    }
}
