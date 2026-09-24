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

    [Fact]
    public void Build_BilledAmountsBackfill_IncludesCountsOnly()
    {
        BackfillSubscriptionBilledAmountsCommand command = new()
        {
            CardBilledUpdated = 3,
            CardBilledSkipped = 1,
            CashBilledSnapshotted = 21,
        };

        string metadata = AuditMetadataBuilder.Build(command);

        using JsonDocument doc = JsonDocument.Parse(metadata);
        doc.RootElement.GetProperty("cardBilledUpdated").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("cardBilledSkipped").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("cashBilledSnapshotted").GetInt32().Should().Be(21);
        doc.RootElement.EnumerateObject().Should().HaveCount(3);
        AssertNoPiiShapedFields(metadata);
    }

    [Fact]
    public void Build_RevenueLedgerBackfill_IncludesCountsOnly()
    {
        BackfillRevenueLedgerCommand command = new()
        {
            Created = 9,
            SkippedAlreadyInLedger = 0,
            SkippedNotBilling = 16,
        };

        string metadata = AuditMetadataBuilder.Build(command);

        using JsonDocument doc = JsonDocument.Parse(metadata);
        doc.RootElement.GetProperty("created").GetInt32().Should().Be(9);
        doc.RootElement.GetProperty("skippedAlreadyInLedger").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("skippedNotBilling").GetInt32().Should().Be(16);
        doc.RootElement.EnumerateObject().Should().HaveCount(3);
        AssertNoPiiShapedFields(metadata);
    }

    [Fact]
    public void BackfillCommands_AreAuditedAsPlatformWide_AndNeverAsSubscriptionActions()
    {
        // MrrInputLoader reads cancellation timestamps out of the audit table by action + target
        // type "Subscription"; a backfill must never look like one of those.
        Pena_e_Arte.Domain.Interfaces.IAuditableCommand[] commands =
            [new BackfillSubscriptionBilledAmountsCommand(), new BackfillRevenueLedgerCommand()];

        foreach (Pena_e_Arte.Domain.Interfaces.IAuditableCommand command in commands)
        {
            command.AuditTargetType.Should().Be(Pena_e_Arte.Domain.Constants.AuditTargetTypes.Platform);
            command.AuditTargetId.Should().Be(Guid.Empty);
            command.AuditStudioId.Should().BeNull();
            command.AuditAction.Should().NotBe(Pena_e_Arte.Domain.Constants.AuditActions.SubscriptionCancelledByAdmin);
            command.AuditAction.Should().NotBe(Pena_e_Arte.Domain.Constants.AuditActions.SubscriptionCancelledByOwner);
        }
        commands[0].AuditAction.Should().Be("Platform.BilledAmountsBackfilled");
        commands[1].AuditAction.Should().Be("Platform.RevenueLedgerBackfilled");
    }
}
