using System.Text.Json;
using FluentAssertions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Common;
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
