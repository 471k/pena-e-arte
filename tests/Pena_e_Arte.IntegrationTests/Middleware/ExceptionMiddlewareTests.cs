using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.IntegrationTests.Middleware;

public class ExceptionMiddlewareTests
{
    private static async Task<(int StatusCode, string Body)> InvokeWithException(Exception ex)
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        ExceptionMiddleware middleware = new(
            _ => throw ex,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private static async Task<int> InvokeWithNoException()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        ExceptionMiddleware middleware = new(
            _ => Task.CompletedTask,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task InvokeAsync_ValidationException_Returns422()
    {
        var failures = new List<ValidationFailure> { new("Field", "Required") };
        (int code, _) = await InvokeWithException(new ValidationException(failures));
        code.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task InvokeAsync_ValidationException_BodyContainsAllErrors()
    {
        var failures = new List<ValidationFailure>
        {
            new("Field1", "Error 1"),
            new("Field2", "Error 2")
        };
        (_, string body) = await InvokeWithException(new ValidationException(failures));
        body.Should().Contain("Error 1");
        body.Should().Contain("Error 2");
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_Returns404()
    {
        (int code, _) = await InvokeWithException(new NotFoundException("Studio", Guid.NewGuid()));
        code.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task InvokeAsync_SlotAlreadyBookedException_Returns409()
    {
        (int code, _) = await InvokeWithException(new SlotAlreadyBookedException());
        code.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task InvokeAsync_DesignAlreadyApprovedException_Returns409()
    {
        (int code, _) = await InvokeWithException(new DesignAlreadyApprovedException());
        code.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task InvokeAsync_TenantSuspendedException_Returns403()
    {
        (int code, _) = await InvokeWithException(new TenantSuspendedException());
        code.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_SubscriptionRequiredException_Returns402()
    {
        (int code, _) = await InvokeWithException(new SubscriptionRequiredException());
        code.Should().Be(StatusCodes.Status402PaymentRequired);
    }

    [Fact]
    public async Task InvokeAsync_BusinessRuleViolationException_Returns422()
    {
        (int code, _) = await InvokeWithException(new BusinessRuleViolationException("Duplicate slug."));
        code.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedAccessException_Returns401()
    {
        (int code, _) = await InvokeWithException(new UnauthorizedAccessException());
        code.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_UnknownException_Returns500()
    {
        (int code, _) = await InvokeWithException(new InvalidOperationException("Something went wrong."));
        code.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_UnknownException_DoesNotLeakExceptionDetails()
    {
        (_, string body) = await InvokeWithException(new InvalidOperationException("Internal DB error"));
        body.Should().NotContain("Internal DB error");
        body.Should().Contain("unexpected error");
    }

    [Fact]
    public async Task InvokeAsync_DomainExceptionBody_ContainsDomainMessage()
    {
        (_, string body) = await InvokeWithException(new NotFoundException("Appointment", Guid.NewGuid()));
        body.Should().Contain("Appointment");
    }

    [Fact]
    public async Task InvokeAsync_ResponseContentType_IsApplicationJson()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        ExceptionMiddleware middleware = new(
            _ => throw new NotFoundException("X", 1),
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThroughWithOriginalStatus()
    {
        int code = await InvokeWithNoException();
        code.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_ResponseBodyIsValidJson()
    {
        (_, string body) = await InvokeWithException(new NotFoundException("Plan", Guid.NewGuid()));

        Action parse = () => JsonDocument.Parse(body);
        parse.Should().NotThrow();
    }
}
