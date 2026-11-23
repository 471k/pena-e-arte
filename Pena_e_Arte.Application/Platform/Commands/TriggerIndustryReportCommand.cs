using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Constants;

namespace Pena_e_Arte.Application.Platform.Commands;

// Audited (D04 follow-up, 23 Nov): platform-wide, so the target is Platform with an empty id.
public record TriggerIndustryReportCommand : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.IndustryReportTriggered;
    public string AuditTargetType => AuditTargetTypes.Platform;
    public Guid AuditTargetId => Guid.Empty;
}

public class TriggerIndustryReportHandler(
    IJobScheduler jobs,
    ILogger<TriggerIndustryReportHandler> logger)
    : IRequestHandler<TriggerIndustryReportCommand>
{
    public Task Handle(TriggerIndustryReportCommand command, CancellationToken ct)
    {
        jobs.TriggerIndustryReportNow();
        logger.LogInformation("Industry report generation triggered by admin");
        return Task.CompletedTask;
    }
}

// Required by the "no endpoint without a validator" rule, even with no properties.
public class TriggerIndustryReportValidator : AbstractValidator<TriggerIndustryReportCommand>
{
    // No properties to validate — validator satisfies the registration convention.
}
