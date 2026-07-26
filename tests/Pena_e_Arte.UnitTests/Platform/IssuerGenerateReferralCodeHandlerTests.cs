using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pena_e_Arte.UnitTests.Platform;

public class IssuerGenerateReferralCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();

    private IssuerGenerateReferralCodeHandler CreateSut() =>
        new(_db, _realtime, NullLogger<IssuerGenerateReferralCodeHandler>.Instance);

    [Fact]
    public async Task Handle_ValidStudio_ReturnsNewActiveCode()
    {
        Guid studioId = await SeedStudio();

        Contracts.Responses.PlatformReferralCodeResponse result =
            await CreateSut().Handle(new IssuerGenerateReferralCodeCommand(studioId), default);

        result.Should().NotBeNull();
        result.StudioId.Should().Be(studioId);
        result.IsActive.Should().BeTrue();
        result.Code.Should().HaveLength(8);
    }

    [Fact]
    public async Task Handle_DeactivatesExistingActiveCodesBeforeGenerating()
    {
        Guid studioId = await SeedStudio();
        ReferralCode old = new() { StudioId = studioId, Code = "OLD12345", IsActive = true };
        _db.ReferralCodes.Add(old);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(new IssuerGenerateReferralCodeCommand(studioId), default);

        _db.ReferralCodes.Single(r => r.Code == "OLD12345").IsActive.Should().BeFalse();
        _db.ReferralCodes.Count(r => r.StudioId == studioId && r.IsActive).Should().Be(1);
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () =>
            CreateSut().Handle(new IssuerGenerateReferralCodeCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ValidStudio_ReturnsShareUrlContainingTheCode()
    {
        Guid studioId = await SeedStudio();

        Contracts.Responses.PlatformReferralCodeResponse result =
            await CreateSut().Handle(new IssuerGenerateReferralCodeCommand(studioId), default);

        result.ShareUrl.Should().Be($"https://tattooos.co/register?ref={result.Code}");
    }

    [Fact]
    public async Task Handle_ValidStudio_CreatesInAppNotificationLogForTheStudio()
    {
        Guid studioId = await SeedStudio();

        Contracts.Responses.PlatformReferralCodeResponse result =
            await CreateSut().Handle(new IssuerGenerateReferralCodeCommand(studioId), default);

        NotificationLog notice = _db.NotificationLogs.Single(n => n.StudioId == studioId);
        notice.Channel.Should().Be(NotificationChannel.InApp);
        notice.RecipientType.Should().Be(NotificationRecipientType.Studio);
        notice.RecipientId.Should().Be(studioId);
        notice.IsSuccess.Should().BeTrue();
        notice.Body.Should().Contain(result.Code);
    }

    [Fact]
    public async Task Handle_ValidStudio_PushesRealtimeNotificationToStudioGroup()
    {
        Guid studioId = await SeedStudio();

        await CreateSut().Handle(new IssuerGenerateReferralCodeCommand(studioId), default);

        await _realtime.Received(1).NotifyStudioAsync(
            studioId, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewCodeHasZeroRedemptionCount()
    {
        Guid studioId = await SeedStudio();

        Contracts.Responses.PlatformReferralCodeResponse result =
            await CreateSut().Handle(new IssuerGenerateReferralCodeCommand(studioId), default);

        result.RedemptionCount.Should().Be(0);
    }

    private async Task<Guid> SeedStudio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "Test Studio",
            Slug = Guid.NewGuid().ToString("N")[..20],
            City = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return studioId;
    }
}
