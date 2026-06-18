using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

public record TriggerIndustryReportCommand : IRequest;

public class TriggerIndustryReportHandler(
    IJobScheduler                          jobs,
    ILogger<TriggerIndustryReportHandler>  logger)
    : IRequestHandler<TriggerIndustryReportCommand>
{
    public Task Handle(TriggerIndustryReportCommand command, CancellationToken ct)
    {
        jobs.TriggerIndustryReportNow();
        logger.LogInformation("Industry report generation triggered by issuer");
        return Task.CompletedTask;
    }
}

// Required by the "no endpoint without a validator" rule, even with no properties.
public class TriggerIndustryReportValidator : AbstractValidator<TriggerIndustryReportCommand>
{
    // No properties to validate — validator satisfies the registration convention.
}
