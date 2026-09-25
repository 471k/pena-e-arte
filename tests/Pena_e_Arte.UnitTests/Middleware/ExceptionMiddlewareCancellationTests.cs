using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.ConsentForms;

namespace Pena_e_Arte.UnitTests.Middleware;

/// <summary>
/// A client that abandons a request is not a server error. Observed in prod: every deploy logged one
/// Error-level "Unhandled exception" (TaskCanceledException in HealthCheckService.CheckHealthAsync)
/// from a Kubernetes probe cancelling its own request during warm-up.
/// </summary>
public class ExceptionMiddlewareCancellationTests
{
    private readonly CapturingLogger<ExceptionMiddleware> _logger = new();

    private static DefaultHttpContext NewContext(CancellationToken requestAborted)
    {
        DefaultHttpContext context = new() { RequestAborted = requestAborted };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private async Task Run(HttpContext context, Exception toThrow) =>
        await new ExceptionMiddleware(_ => throw toThrow, _logger).InvokeAsync(context);

    [Fact]
    public async Task ClientCancelledRequest_Is499_NoErrorLog_AndNoBodyWritten()
    {
        using CancellationTokenSource aborted = new();
        aborted.Cancel();
        DefaultHttpContext context = NewContext(aborted.Token);

        await Run(context, new TaskCanceledException("A task was canceled."));

        context.Response.StatusCode.Should().Be(499);
        _logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task CancellationThatDidNotComeFromTheClient_StillSurfacesAsA500AndAnErrorLog()
    {
        // The request was NOT aborted: a server-side timeout / bug must not be swallowed.
        DefaultHttpContext context = NewContext(CancellationToken.None);

        await Run(context, new OperationCanceledException("server-side"));

        context.Response.StatusCode.Should().Be(500);
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ARealErrorOnAnAbortedRequest_IsStillLogged_NotMistakenForACancellation()
    {
        using CancellationTokenSource aborted = new();
        aborted.Cancel();
        DefaultHttpContext context = NewContext(aborted.Token);

        await Run(context, new InvalidOperationException("boom"));

        context.Response.StatusCode.Should().Be(500);
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task OrdinaryMappedExceptions_AreUnchanged()
    {
        DefaultHttpContext context = NewContext(CancellationToken.None);

        await Run(context, new NotFoundException("Studio", Guid.NewGuid()));

        context.Response.StatusCode.Should().Be(404);
        _logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }
}
