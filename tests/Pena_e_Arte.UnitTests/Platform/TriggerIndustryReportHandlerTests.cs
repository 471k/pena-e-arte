using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Interfaces;
using Xunit;

namespace Pena_e_Arte.UnitTests.Platform;

public class TriggerIndustryReportHandlerTests
{
    [Fact]
    public async Task Handle_CallsTriggerIndustryReportNow()
    {
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        TriggerIndustryReportHandler sut = new(
            scheduler,
            NullLogger<TriggerIndustryReportHandler>.Instance);

        await sut.Handle(new TriggerIndustryReportCommand(), CancellationToken.None);

        scheduler.Received(1).TriggerIndustryReportNow();
    }

    [Fact]
    public async Task Handle_CompletesSuccessfully_WithoutThrow()
    {
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        TriggerIndustryReportHandler sut = new(
            scheduler,
            NullLogger<TriggerIndustryReportHandler>.Instance);

        Exception? ex = await Record.ExceptionAsync(
            () => sut.Handle(new TriggerIndustryReportCommand(), CancellationToken.None));

        Assert.Null(ex);
    }
}
