using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Pena_e_Arte.Domain.Exceptions;
using Stripe;

namespace Pena_e_Arte.API.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>nginx's de-facto "Client Closed Request" — not a real HTTP status, but the widely used
    /// convention for a request the client abandoned before the server answered.</summary>
    private const int ClientClosedRequest = 499;

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        // A client that gives up on a request (closed tab, dropped connection, a Kubernetes probe hitting
        // its own timeout) surfaces here as an OperationCanceledException tied to the request's OWN
        // cancellation token. That is not a server fault: don't log it as an error (it made every prod
        // deploy log one "Unhandled exception" from a cancelled health probe) and don't try to write an
        // error body to a connection nobody is reading. Deliberately gated on RequestAborted, so a
        // cancellation that did NOT come from the client (a server-side timeout, a bug) still falls
        // through to the 500 + error log below.
        if (ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request cancelled by the client");
            if (!context.Response.HasStarted)
                context.Response.StatusCode = ClientClosedRequest;
            return;
        }

        (int statusCode, string message, string? code) = ex switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid or missing request body.", (string?)null),
            JsonException => (StatusCodes.Status400BadRequest, "Invalid JSON in request body.", null),
            ValidationException ve => (StatusCodes.Status422UnprocessableEntity,
                                                string.Join("; ", ve.Errors.Select(e => e.ErrorMessage)), null),
            NotFoundException => (StatusCodes.Status404NotFound, ex.Message, null),
            ConflictException => (StatusCodes.Status409Conflict, ex.Message, null),
            SlotAlreadyBookedException => (StatusCodes.Status409Conflict, ex.Message, null),
            DuplicateNiptException => (StatusCodes.Status409Conflict, ex.Message, null),
            // Unique-index race (e.g. two payment attempts for one appointment) — 1062 = duplicate key
            DbUpdateException { InnerException: MySqlException { Number: 1062 } }
                                            => (StatusCodes.Status409Conflict,
                                                "This action was already completed by another request. Refresh and try again.", null),
            DesignAlreadyApprovedException => (StatusCodes.Status409Conflict, ex.Message, null),
            ConsentFormAlreadySignedException => (StatusCodes.Status409Conflict, ex.Message, null),
            ForbiddenException => (StatusCodes.Status403Forbidden, ex.Message, null),
            ImpersonationScopeException => (StatusCodes.Status403Forbidden, ex.Message, "IMPERSONATION_SCOPE_DENIED"),
            TenantSuspendedException => (StatusCodes.Status403Forbidden, ex.Message, "STUDIO_SUSPENDED"),
            SubscriptionRequiredException => (StatusCodes.Status402PaymentRequired, ex.Message, null),
            PlanLimitExceededException => (StatusCodes.Status403Forbidden, ex.Message, "PLAN_LIMIT_EXCEEDED"),
            ManualReminderQuotaExceededException => (StatusCodes.Status429TooManyRequests, ex.Message, "MANUAL_REMINDER_QUOTA_EXCEEDED"),
            PasswordResetTokenInvalidException => (StatusCodes.Status422UnprocessableEntity, ex.Message, "RESET_TOKEN_INVALID"),
            ChangeEmailTokenInvalidException => (StatusCodes.Status422UnprocessableEntity, ex.Message, "CHANGE_EMAIL_TOKEN_INVALID"),
            BusinessRuleViolationException => (StatusCodes.Status422UnprocessableEntity, ex.Message, null),
            PaymentProviderNotConnectedException => (StatusCodes.Status422UnprocessableEntity, ex.Message, "PAYMENT_PROVIDER_NOT_CONNECTED"),
            ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, ex.Message, null),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, ex.Message, null),
            StripeException stripeEx => (StatusCodes.Status502BadGateway,
                                                stripeEx.StripeError?.Message ?? stripeEx.Message, null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null)
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        object body = code is not null
            ? new { status = statusCode, message, code }
            : new { status = statusCode, message };

        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
