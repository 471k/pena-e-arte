using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;

namespace Pena_e_Arte.UnitTests.Jobs;

public class R2ExportJobTests
{
    private readonly IR2ExportService _exportService = Substitute.For<IR2ExportService>();

    private R2ExportJob CreateSut() => new(_exportService, NullLogger<R2ExportJob>.Instance);

    [Fact]
    public async Task RunAsync_DelegatesToExportServiceAndDoesNotThrow()
    {
        _exportService.RunAsync(Arg.Any<CancellationToken>()).Returns(new R2ExportResult(3, 5, 0));

        Func<Task> act = () => CreateSut().RunAsync();

        await act.Should().NotThrowAsync();
        await _exportService.Received(1).RunAsync(Arg.Any<CancellationToken>());
    }
}
