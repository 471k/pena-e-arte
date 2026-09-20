using FluentAssertions;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Social;

public class SocialCommandAuditTests
{
    private readonly Guid _id = Guid.NewGuid();

    [Fact]
    public void ArtistSubjectCommands_AuditAsArtistTargets()
    {
        (IAuditableCommand Command, string Action)[] cases =
        [
            (new UpdateSocialHandleCommand(SocialLinkSubjectType.Artist, _id, SocialPlatform.X, "h"), AuditActions.SocialHandleUpdated),
            (new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Artist, _id, SocialPlatform.X), AuditActions.SocialVerificationRequested),
            (new VerifySocialBioCodeCommand(SocialLinkSubjectType.Artist, _id, SocialPlatform.X), AuditActions.SocialVerificationAttempted),
            (new DisconnectSocialAccountCommand(SocialLinkSubjectType.Artist, _id, SocialPlatform.X), AuditActions.SocialDisconnected),
            (new DisconnectInstagramCommand(_id), AuditActions.SocialDisconnected),
        ];

        foreach ((IAuditableCommand command, string action) in cases)
        {
            command.AuditAction.Should().Be(action);
            command.AuditTargetType.Should().Be(AuditTargetTypes.Artist);
            command.AuditTargetId.Should().Be(_id);
        }
    }

    [Fact]
    public void StudioSubjectCommands_AuditAsStudioTargets()
    {
        IAuditableCommand[] commands =
        [
            new UpdateSocialHandleCommand(SocialLinkSubjectType.Studio, _id, SocialPlatform.X, "h"),
            new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Studio, _id, SocialPlatform.X),
            new VerifySocialBioCodeCommand(SocialLinkSubjectType.Studio, _id, SocialPlatform.X),
            new DisconnectSocialAccountCommand(SocialLinkSubjectType.Studio, _id, SocialPlatform.X),
        ];

        commands.Should().OnlyContain(c => c.AuditTargetType == AuditTargetTypes.Studio && c.AuditTargetId == _id);
    }

    [Fact]
    public void AuditActions_FollowTheNounDotVerbConvention()
    {
        string[] actions =
        [
            AuditActions.SocialHandleUpdated, AuditActions.SocialVerificationRequested,
            AuditActions.SocialVerificationAttempted, AuditActions.SocialDisconnected,
            AuditActions.SocialConnectedViaOAuth,
        ];

        actions.Should().OnlyContain(a => a.StartsWith("SocialLink.") && a.Split('.').Length == 2);
    }
}
