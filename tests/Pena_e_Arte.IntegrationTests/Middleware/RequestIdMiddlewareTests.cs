using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Pena_e_Arte.API.Middleware;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Pena_e_Arte.IntegrationTests.Middleware;

public class RequestIdMiddlewareTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        public readonly List<LogEvent> Events = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public async Task InvokeAsync_PushesRequestIdOntoLogContext_VisibleToDownstreamLogCalls()
    {
        CapturingSink sink = new();
        Logger scopedLogger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        ILogger previous = Log.Logger;
        Log.Logger = scopedLogger;

        try
        {
            DefaultHttpContext context = new() { TraceIdentifier = "trace-abc-123" };

            RequestIdMiddleware middleware = new(_ =>
            {
                Log.Information("inside pipeline");
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);
        }
        finally
        {
            Log.Logger = previous;
            scopedLogger.Dispose();
        }

        sink.Events.Should().ContainSingle();
        sink.Events[0].Properties.Should().ContainKey("request_id");
        sink.Events[0].Properties["request_id"].ToString().Should().Contain("trace-abc-123");
    }

    [Fact]
    public async Task InvokeAsync_LogContextScopeDisposedAfterRequest_DoesNotLeakToLaterLogs()
    {
        CapturingSink sink = new();
        Logger scopedLogger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        ILogger previous = Log.Logger;
        Log.Logger = scopedLogger;

        try
        {
            DefaultHttpContext context = new() { TraceIdentifier = "trace-xyz" };
            RequestIdMiddleware middleware = new(_ => Task.CompletedTask);
            await middleware.InvokeAsync(context);

            Log.Information("after pipeline");
        }
        finally
        {
            Log.Logger = previous;
            scopedLogger.Dispose();
        }

        sink.Events.Should().ContainSingle();
        sink.Events[0].Properties.Should().NotContainKey("request_id");
    }

    [Fact]
    public async Task InvokeAsync_CallsNext()
    {
        bool called = false;
        RequestIdMiddleware middleware = new(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(new DefaultHttpContext());

        called.Should().BeTrue();
    }
}
