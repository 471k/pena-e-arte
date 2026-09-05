using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

// No-op, matching NullR2Service: with no R2 configured at all there is nothing to export, and
// the recurring job must still be able to run without crashing in an R2-less environment.
internal sealed class NullR2ExportService : IR2ExportService
{
    public Task<R2ExportResult> RunAsync(CancellationToken ct) =>
        Task.FromResult(new R2ExportResult(0, 0, 0));
}
