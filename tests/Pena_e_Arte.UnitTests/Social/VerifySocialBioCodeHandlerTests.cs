using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Contracts.Responses.Social;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Social;

public class VerifySocialBioCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISocialBioCheckerFactory _checkerFactory = Substitute.For<ISocialBioCheckerFactory>();
    private readonly ISocialBioChecker _checker = Substitute.For<ISocialBioChecker>();
    private readonly Guid _studioId = Guid.NewGuid();

    public VerifySocialBioCodeHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _checker.IsSupported.Returns(true);
        _checkerFactory.GetChecker(Arg.Any<SocialPlatform>()).Returns(_checker);
    }

    private VerifySocialBioCodeHandler CreateSut() =>
        new(_db, _tenant, _checkerFactory, NullLogger<VerifySocialBioCodeHandler>.Instance);

    private async Task<SocialAccountLink> SeedPendingLink(DateTime? expiresAt = null)
    {
        SocialAccountLink link = new()
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = _studioId,
            Platform = SocialPlatform.YouTube,
            Handle = "studio",
            PendingVerificationCode = "PENA-ABC123",
            PendingCodeExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1),
        };
        _db.SocialAccountLinks.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    [Fact]
    public async Task Handle_NoPendingCode_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new VerifySocialBioCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.YouTube), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ExpiredCode_ThrowsBusinessRuleViolationException()
    {
        await SeedPendingLink(expiresAt: DateTime.UtcNow.AddHours(-1));

        Func<Task> act = () => CreateSut().Handle(
            new VerifySocialBioCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.YouTube), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_UnsupportedPlatform_ThrowsBusinessRuleViolationException()
    {
        await SeedPendingLink();
        _checker.IsSupported.Returns(false);

        Func<Task> act = () => CreateSut().Handle(
            new VerifySocialBioCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.YouTube), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_CodeNotFoundInBio_ReturnsNotVerifiedAndKeepsPendingCode()
    {
        await SeedPendingLink();
        _checker.BioContainsCodeAsync("studio", "PENA-ABC123", Arg.Any<CancellationToken>()).Returns(false);

        SocialVerifyResultResponse result = await CreateSut().Handle(
            new VerifySocialBioCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.YouTube), default);

        result.Verified.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
        _db.SocialAccountLinks.Single().PendingVerificationCode.Should().Be("PENA-ABC123");
    }

    [Fact]
    public async Task Handle_CodeFoundInBio_VerifiesAndClearsPendingCode()
    {
        await SeedPendingLink();
        _checker.BioContainsCodeAsync("studio", "PENA-ABC123", Arg.Any<CancellationToken>()).Returns(true);

        SocialVerifyResultResponse result = await CreateSut().Handle(
            new VerifySocialBioCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.YouTube), default);

        result.Verified.Should().BeTrue();

        SocialAccountLink link = _db.SocialAccountLinks.Single();
        link.IsVerified.Should().BeTrue();
        link.VerificationMethod.Should().Be(SocialVerificationMethod.ManualBioCode);
        link.PendingVerificationCode.Should().BeNull();
        link.PendingCodeExpiresAt.Should().BeNull();
        link.VerifiedAt.Should().NotBeNull();
    }
}
