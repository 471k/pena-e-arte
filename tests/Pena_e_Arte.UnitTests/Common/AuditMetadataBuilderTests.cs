using System.Text.Json;
using FluentAssertions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.UnitTests.Common;

public class AuditMetadataBuilderTests
{
    private static void AssertNoPiiShapedFields(string metadata)
    {
        using JsonDocument doc = JsonDocument.Parse(metadata);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            prop.Name.Should().NotMatchRegex("(?i)email|phone|address|note|firstname|lastname");
    }

    [Fact]
    public void Build_SocialLinkCommands_IncludePlatformOnlyNeverTheHandle()
    {
        Guid subject = Guid.NewGuid();
        object[] commands =
        [
            new UpdateSocialHandleCommand(SocialLinkSubjectType.Artist, subject, SocialPlatform.TikTok, "secret_handle"),
            new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Artist, subject, SocialPlatform.TikTok),
            new VerifySocialBioCodeCommand(SocialLinkSubjectType.Artist, subject, SocialPlatform.TikTok),
            new DisconnectSocialAccountCommand(SocialLinkSubjectType.Artist, subject, SocialPlatform.TikTok),
        ];

        foreach (object command in commands)
        {
            string metadata = AuditMetadataBuilder.Build(command);

            metadata.Should().Be("{\"platform\":\"TikTok\"}");
            metadata.Should().NotContain("secret_handle");
            AssertNoPiiShapedFields(metadata);
        }
    }

    [Fact]
    public void Build_DisconnectInstagramCommand_IncludesPlatformOnly()
    {
        AuditMetadataBuilder.Build(new DisconnectInstagramCommand(Guid.NewGuid())).Should().Be("{\"platform\":\"Instagram\"}");
    }

    [Fact]
    public void Build_UnknownCommand_ReturnsEmptyObject()
    {
        string metadata = AuditMetadataBuilder.Build(new object());
        metadata.Should().Be("{}");
    }

    [Fact]
    public void Build_ExtendTrialCommand_IncludesAdditionalDaysOnly()
    {
        ExtendTrialCommand command = new(Guid.NewGuid(), new ExtendTrialRequest(14));

        string metadata = AuditMetadataBuilder.Build(command);

        metadata.Should().Contain("\"additionalDays\":14");
        AssertNoPiiShapedFields(metadata);
    }

    [Fact]
    public void Build_ActivateSubscriptionManuallyCommand_IncludesPlanIdOnly()
    {
        Guid planId = Guid.NewGuid();
        ActivateSubscriptionManuallyCommand command = new(Guid.NewGuid(), planId, "some free-text note");

        string metadata = AuditMetadataBuilder.Build(command);

        metadata.Should().Contain(planId.ToString());
        metadata.Should().NotContain("some free-text note");
        AssertNoPiiShapedFields(metadata);
    }

    [Fact]
    public void Build_UpdatePlanCommand_IncludesPlanNameOnly()
    {
        UpdatePlanCommand command = new(
            Guid.NewGuid(),
            new UpdatePlanRequest("Professional", 17, []));

        string metadata = AuditMetadataBuilder.Build(command);

        metadata.Should().Contain("Professional");
        AssertNoPiiShapedFields(metadata);
    }
}
