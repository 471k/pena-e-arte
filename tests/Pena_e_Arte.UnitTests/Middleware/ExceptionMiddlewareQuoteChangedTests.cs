using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.ConsentForms;

namespace Pena_e_Arte.UnitTests.Middleware;

/// <summary>E05b-2 (O07): a changed cancellation quote reaches the dialog as 409 with a code it can act on.</summary>
public class ExceptionMiddlewareQuoteChangedTests
{
    [Fact]
    public async Task CancellationQuoteChanged_Is409_WithItsCode()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        await new ExceptionMiddleware(_ => throw new CancellationQuoteChangedException(), new CapturingLogger<ExceptionMiddleware>())
            .InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Position = 0;
        using JsonDocument body = await JsonDocument.ParseAsync(context.Response.Body);
        body.RootElement.GetProperty("code").GetString().Should().Be("CANCELLATION_QUOTE_CHANGED");
        body.RootElement.GetProperty("message").GetString().Should().Contain("Nothing was cancelled");
    }
}
